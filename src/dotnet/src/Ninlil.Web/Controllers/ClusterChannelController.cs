using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.Abstractions.Routing;
using Ninlil.Dns.Server.Cluster.Authentication;
using Ninlil.Dns.Server.Cluster.Channel;
using Ninlil.Dns.Server.Cluster.Heartbeat;
using Ninlil.Dns.Server.Cluster.Join;
using Ninlil.Dns.Server.Core.ClusterSync;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller is the receiving end of the node-to-node cluster channel. Every action
///     authenticates a peer node by a TSIG-key HMAC signature
///     (<see cref="ClusterTsigAuthenticationDefaults.AuthenticationScheme" />, checked by
///     <see cref="ClusterTsigAuthenticationHandler" />), never an operator's JWT or API token;
///     no permission is checked. The handler accepts only a known peer's own key, except that an
///     unused invite key may sign <see cref="JoinComplete" />, so every other action, config sync
///     included, only ever hears from an established peer. The operator-facing side of the same
///     peer registry is
///     <see cref="ClusterController" />; keeping the two apart means the controller alone tells a
///     reader which authentication applies.
/// </summary>
/// <param name="joinService">Handles the joining node's callback.</param>
/// <param name="heartbeatReceiver">Checks a peer's heartbeat and re-pins its certificate.</param>
/// <param name="configSyncAppliers">
///     Every registered <see cref="IClusterConfigSyncApplier" /> (see <see cref="ConfigSync" />).
/// </param>
/// <param name="userRepository">
///     Answers <see cref="UsernameCheck" />, read directly rather than through
///     <c>UserManager&lt;TUser&gt;</c>.
/// </param>
/// <param name="scopeFactory">
///     Opens the replica read scope in which <see cref="QueryStats" /> resolves the query log
///     repository.
/// </param>
/// <param name="logger">Records unrecognized or failed config-sync envelopes.</param>
[ApiController]
[Route("api/cluster/channel")]
[Authorize(AuthenticationSchemes = ClusterTsigAuthenticationDefaults.AuthenticationScheme)]
public class ClusterChannelController(
    ClusterJoinService joinService,
    ClusterHeartbeatReceiver heartbeatReceiver,
    IEnumerable<IClusterConfigSyncApplier> configSyncAppliers,
    IUserRepository userRepository,
    IServiceScopeFactory scopeFactory,
    ILogger<ClusterChannelController> logger
) : ControllerBase
{
    /// <summary>
    ///     This action answers a peer's heartbeat with 200, after checking that the body names the
    ///     peer the key belongs to and storing a newly announced certificate thumbprint (see
    ///     <see cref="ClusterHeartbeatReceiver" />). Each node's <c>ClusterHeartbeatBackgroundService</c>
    ///     sets a peer's <see cref="ClusterNodeEntity.Status" /> from whether its own probe of that
    ///     peer succeeded, so each node trusts its own view of a peer's health rather than
    ///     anything the peer reports. An invite key cannot reach this action.
    /// </summary>
    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat(
        [FromBody] ClusterHeartbeatRequest request,
        CancellationToken stoppingToken
    )
    {
        Guid authenticatedRemoteNodeId = Guid.Parse(
            User.FindFirstValue(ClusterTsigAuthenticationDefaults.RemoteNodeIdClaimType)
        );

        await heartbeatReceiver.HandleHeartbeatAsync(
            authenticatedRemoteNodeId,
            request,
            stoppingToken
        );

        return Ok();
    }

    /// <summary>
    ///     This action receives the joining node's callback, the second half of the join handshake
    ///     (see <see cref="ClusterJoinService.HandleJoinCompleteAsync" />). The TSIG key name comes
    ///     from the identity <see cref="ClusterTsigAuthenticationHandler" /> authenticated, never
    ///     from the request body. It is the one action an unused, unexpired invite key may sign;
    ///     the route must stay <see cref="ClusterTsigAuthenticationDefaults.JoinCompletePath" />.
    /// </summary>
    [HttpPost("join-complete")]
    public async Task<IActionResult> JoinComplete(
        [FromBody] ClusterJoinCompleteRequest request,
        CancellationToken stoppingToken
    )
    {
        string authenticatedTsigKeyName = User.FindFirstValue(ClaimTypes.Name);

        await joinService.HandleJoinCompleteAsync(authenticatedTsigKeyName, request, stoppingToken);

        return Ok();
    }

    /// <summary>
    ///     This action receives one config-sync envelope from a peer and passes it to the
    ///     <see cref="IClusterConfigSyncApplier" /> whose
    ///     <see cref="IClusterConfigSyncApplier.EntityType" /> matches
    ///     <see cref="ClusterConfigSyncEnvelope.EntityType" />. Every synced kind of data (filter
    ///     lists and entries, access-control rules, rewrites, forwarders, users, roles, settings
    ///     sections) comes through this one endpoint.
    ///     <para>
    ///         It always returns 200, even for an entity type this node does not know (logged and
    ///         ignored), so a newer peer sending a kind an older node cannot handle does not look
    ///         like a channel failure. Accepting, rejecting and last-write-wins are decided by the
    ///         applier (see <see cref="IClusterConfigSyncApplier" />).
    ///     </para>
    /// </summary>
    [HttpPost("config-sync")]
    public async Task<IActionResult> ConfigSync(
        [FromBody] ClusterConfigSyncEnvelope envelope,
        CancellationToken stoppingToken
    )
    {
        IClusterConfigSyncApplier applier = configSyncAppliers.FirstOrDefault(candidate =>
            candidate.EntityType == envelope.EntityType
        );

        if (applier is null)
        {
            logger.LogWarning(
                "Received a config-sync envelope for an unrecognized entity type "
                    + "'{EntityType}' -- ignored.",
                envelope.EntityType
            );
            return Ok();
        }

        try
        {
            await applier.ApplyAsync(envelope, stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A malformed or rejected envelope is logged in full and still answered with 200 rather
            // than a 500. The sender treats delivery as best-effort (see
            // ClusterConfigSyncBroadcaster) and would not retry differently anyway.
            logger.LogError(
                exception,
                "Failed to apply a config-sync envelope for {EntityType} '{EntityKey}'.",
                envelope.EntityType,
                envelope.EntityKey
            );
        }

        return Ok();
    }

    /// <summary>
    ///     This action tells a peer whether a username exists on this node. A peer's
    ///     <c>ClusterUsernameUniquenessChecker</c> calls it when creating a user. It is a plain
    ///     lookup with no lock or reservation, so two nodes creating the same name within moments
    ///     of each other can both succeed; <see cref="ClusterSyncConflictResolver" />, applied by
    ///     <c>UserClusterConfigSyncApplier</c>, resolves and flags that collision once the nodes
    ///     sync.
    /// </summary>
    [HttpPost("username-check")]
    public async Task<ActionResult<ClusterUsernameCheckResult>> UsernameCheck(
        [FromBody] ClusterUsernameCheckRequest request,
        CancellationToken stoppingToken
    )
    {
        UserEntity existing = await userRepository.FindByNormalizedUserNameAsync(
            request.NormalizedUserName,
            stoppingToken
        );

        return Ok(new ClusterUsernameCheckResult(existing is not null));
    }

    /// <summary>
    ///     This action returns this node's query counts (last hour, last 24 hours, all retained)
    ///     for the cluster dashboard. A peer's <c>ClusterQueryStatsAggregator</c> calls it when an
    ///     operator opens that peer's Cluster page. The counts are computed from the query log on
    ///     every call and not cached; a dashboard load can afford that. They are read through a
    ///     scope of their own that uses a read replica when one is eligible: a count that is a few
    ///     seconds old is invisible next to a query log the writer batches for up to a second.
    ///     The request scope cannot choose a replica because the peer authentication has already
    ///     read the registry there.
    /// </summary>
    [HttpPost("query-stats")]
    public async Task<ActionResult<ClusterQueryStatsSnapshot>> QueryStats(
        [FromBody] ClusterQueryStatsRequest request,
        CancellationToken stoppingToken
    )
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        await using AsyncServiceScope scope = scopeFactory.CreateReplicaReadScope();
        IDnsQueryLogRepository queryLogRepository =
            scope.ServiceProvider.GetRequiredService<IDnsQueryLogRepository>();

        int queriesLastHour = await queryLogRepository.CountAsync(
            new DnsQueryLogSearchCriteria(FromUtc: now - TimeSpan.FromHours(1)),
            stoppingToken
        );
        int queriesLast24Hours = await queryLogRepository.CountAsync(
            new DnsQueryLogSearchCriteria(FromUtc: now - TimeSpan.FromHours(24)),
            stoppingToken
        );
        int totalRetainedQueries = await queryLogRepository.CountAsync(
            new DnsQueryLogSearchCriteria(),
            stoppingToken
        );

        return Ok(
            new ClusterQueryStatsSnapshot(queriesLastHour, queriesLast24Hours, totalRetainedQueries)
        );
    }
}
