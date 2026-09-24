using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Cluster.Authentication;
using Ninlil.Dns.Server.Cluster.Channel;
using Ninlil.Dns.Server.Cluster.Join;
using Ninlil.Dns.Server.Core.ClusterSync;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller is the inbound half of the node-to-node cluster channel: every action here
///     authenticates a <em>peer node</em>, via a
///     pre-shared TSIG signature (<see cref="ClusterTsigAuthenticationDefaults.AuthenticationScheme" />
///     -- see <see cref="ClusterTsigAuthenticationHandler" />), never a human operator's JWT. This
///     is deliberately a separate controller from <see cref="ClusterController" /> (the
///     JWT-authenticated, human-operator-facing surface for the exact same underlying peer
///     registry) rather than the same controller carrying mixed authentication requirements
///     action-by-action -- a reader (or an auditor) can tell which authentication model applies
///     to a given endpoint from its controller alone, never a per-action attribute they might
///     miss.
/// </summary>
/// <param name="joinService">The service this controller delegates join-callback handling to.</param>
/// <param name="configSyncAppliers">
///     Every registered <see cref="IClusterConfigSyncApplier" /> -- see
///     <see cref="ConfigSync" />.
/// </param>
/// <param name="userRepository">
///     Looked up directly (not through <c>UserManager&lt;TUser&gt;</c>) for
///     <see cref="UsernameCheck" />'s own read-only existence check -- see that action's own doc
///     comment.
/// </param>
/// <param name="queryLogRepository">
///     Read directly by <see cref="QueryStats" /> to build this node's own live query-stats
///     snapshot -- see that action's own doc comment.
/// </param>
/// <param name="logger">The logger an unrecognized/failed config-sync envelope is recorded to.</param>
[ApiController]
[Route("api/cluster/channel")]
[Authorize(AuthenticationSchemes = ClusterTsigAuthenticationDefaults.AuthenticationScheme)]
public class ClusterChannelController(
    ClusterJoinService joinService,
    IEnumerable<IClusterConfigSyncApplier> configSyncAppliers,
    IUserRepository userRepository,
    IDnsQueryLogRepository queryLogRepository,
    ILogger<ClusterChannelController> logger
) : ControllerBase
{
    /// <summary>
    ///     This action answers a peer's own heartbeat probe. A successfully authenticated request
    ///     reaching this action at all is the entire trust decision -- this node's own
    ///     <c>ClusterHeartbeatBackgroundService</c> updates the calling peer's own
    ///     <see cref="ClusterNodeEntity.Status" /> purely based on whether its own probe against
    ///     that peer succeeded, never on anything this action returns beyond a bare success
    ///     status; see that service's own doc comment for why each node's own, locally-observed
    ///     view of a peer's health is this project's deliberately chosen source of truth, not a
    ///     value the peer reports about itself.
    /// </summary>
    [HttpPost("heartbeat")]
    public IActionResult Heartbeat([FromBody] ClusterHeartbeatRequest request) => Ok();

    /// <summary>
    ///     This action receives a joining node's own callback, completing the second half of the
    ///     join handshake -- see <see cref="ClusterJoinService.HandleJoinCompleteAsync" /> for the
    ///     real logic this delegates to. The caller's own successfully-authenticated TSIG key name
    ///     (the exact key the invite this joiner is redeeming carried) is read from the
    ///     authenticated identity <see cref="ClusterTsigAuthenticationHandler" /> already
    ///     established, never re-derived from the request body itself.
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
    ///     This action receives one operational-config-sync envelope from a peer and
    ///     dispatches it to whichever registered <see cref="IClusterConfigSyncApplier" />'s own
    ///     <see cref="IClusterConfigSyncApplier.EntityType" /> matches
    ///     <see cref="ClusterConfigSyncEnvelope.EntityType" /> -- the one inbound endpoint every
    ///     synced category (filter lists, filter list entries, access-control rules, rewrites,
    ///     forwarders, settings sections) shares, per this mechanism's own "one generic envelope,
    ///     dispatched by a discriminator" design.
    ///     <para>
    ///         Always answers <c>200 OK</c>, even for an entity type this node does not recognize
    ///         (logged, not rejected) -- a newer peer sending a category an older node has not
    ///         been upgraded to understand yet must never look like a channel failure; the actual
    ///         accept/reject/last-write-wins decision lives entirely inside the matched applier
    ///         itself, per <see cref="IClusterConfigSyncApplier" />'s own doc comment.
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
            // A malformed or otherwise-rejected envelope from one peer must never surface as an
            // unhandled 500 that could confuse that peer's own delivery logging -- log it here,
            // in full, and still answer 200: the sender already treats delivery as best-effort
            // (see ClusterConfigSyncBroadcaster's own doc comment), so there is no retry this
            // response code could usefully trigger differently.
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
    ///     This action answers the cluster's own best-effort, cluster-wide username-uniqueness
    ///     check -- a plain, read-only existence lookup against this node's own
    ///     <see cref="IUserRepository" />, called by a peer's own
    ///     <c>ClusterUsernameUniquenessChecker</c> at user-creation time on that peer. This is
    ///     deliberately the entire check: no lock, no reservation, no two-phase commit -- a real,
    ///     if rare, race (two nodes creating the same username within the same few hundred
    ///     milliseconds, each seeing the other as not-yet-existing) is still possible, and is
    ///     exactly the case the cluster's own last-write-wins collision resolution
    ///     (<see cref="ClusterSyncConflictResolver" />, applied by
    ///     <c>UserClusterConfigSyncApplier</c>) exists to catch and flag once the two nodes'
    ///     config-sync broadcasts actually reach each other.
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
    ///     This action answers the aggregate cluster dashboard's per-peer query-stats
    ///     request -- a plain, read-only count against this node's own persistent query log
    ///     (<see cref="IDnsQueryLogRepository" />), called by a peer's own
    ///     <c>ClusterQueryStatsAggregator</c> when an operator opens that peer's own "Cluster"
    ///     page. Always computed live at request time, never cached -- a dashboard load tolerates
    ///     real query-log I/O far better than a DNS query answer ever could, and there is no
    ///     other consumer of this endpoint to protect from that cost.
    /// </summary>
    [HttpPost("query-stats")]
    public async Task<ActionResult<ClusterQueryStatsSnapshot>> QueryStats(
        [FromBody] ClusterQueryStatsRequest request,
        CancellationToken stoppingToken
    )
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

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
