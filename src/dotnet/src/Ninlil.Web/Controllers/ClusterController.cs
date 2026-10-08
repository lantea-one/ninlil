using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Cluster.Invite;
using Ninlil.Dns.Server.Cluster.Join;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.Core.ClusterSync;
using Ninlil.Web.Examples.Cluster;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller manages this node's cluster membership for an operator: listing, renaming
///     and removing known peers, both halves of the invite-code join handshake (the logic lives in
///     <see cref="ClusterInviteService" /> and <see cref="ClusterJoinService" />), and the
///     cluster query-stats dashboard. It uses the usual admin sign-in. Peers talk to each other
///     through <see cref="ClusterChannelController" /> instead, which uses TSIG. Reads need
///     <c>clusternodes:read</c> and changes need <c>clusternodes:write</c>.
/// </summary>
/// <param name="clusterNodeRepository">Reads and writes peer rows.</param>
/// <param name="tsigKeyRepository">
///     Finds and removes a removed peer's channel key (see <see cref="RemoveNode" />).
/// </param>
/// <param name="unitOfWork">Commits staged changes.</param>
/// <param name="inviteService">Creates invites.</param>
/// <param name="joinService">Completes a join with an invite code.</param>
/// <param name="queryLogRepository">Supplies this node's counts for <see cref="Stats" />.</param>
/// <param name="queryStatsAggregator">
///     Collects each peer's counts for <see cref="Stats" /> (see
///     <see cref="IClusterQueryStatsAggregator" />).
/// </param>
/// <param name="clusterOptions">This node's cluster settings, read by <see cref="Stats" /> to label this node's row.</param>
[ApiController]
[Route("api/cluster/nodes")]
public class ClusterController(
    IClusterNodeRepository clusterNodeRepository,
    ITsigKeyRepository tsigKeyRepository,
    IUnitOfWork unitOfWork,
    ClusterInviteService inviteService,
    ClusterJoinService joinService,
    IDnsQueryLogRepository queryLogRepository,
    IClusterQueryStatsAggregator queryStatsAggregator,
    IOptionsMonitor<ClusterOptions> clusterOptions
) : ControllerBase
{
    /// <summary>
    ///     This action lists the peer nodes in this node's registry, one page at a time. Requires
    ///     <c>clusternodes:read</c>.
    /// </summary>
    [HttpGet]
    [RequirePermission("clusternodes:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ClusterNodeDetailPagedResultExample))]
    public async Task<ActionResult<PagedResult<ClusterNodeDetail>>> List(
        [FromQuery] PaginationQuery pagination,
        CancellationToken stoppingToken
    )
    {
        (int skip, int take) = (pagination ?? new PaginationQuery()).Normalize();

        // The page and the total count are both computed by the storage engine (ORDER
        // BY/OFFSET/LIMIT or the MongoDB equivalent), not by loading every peer into memory.
        IReadOnlyList<ClusterNodeEntity> nodes = await clusterNodeRepository.SearchAsync(
            skip,
            take,
            stoppingToken
        );
        int totalCount = await clusterNodeRepository.CountAsync(stoppingToken);

        return Ok(
            new PagedResult<ClusterNodeDetail>([.. nodes.Select(ToDetail)], skip, take, totalCount)
        );
    }

    /// <summary>
    ///     This action returns one peer by its local registry id. Returns 404 for an unknown id.
    ///     Requires <c>clusternodes:read</c>.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("clusternodes:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ClusterNodeDetailExample))]
    public async Task<ActionResult<ClusterNodeDetail>> Get(Guid id, CancellationToken stoppingToken)
    {
        ClusterNodeEntity node = await clusterNodeRepository.GetByIdAsync(id, stoppingToken);
        if (node is null)
            throw new NotFoundException("ClusterNode", id.ToString());

        return Ok(ToDetail(node));
    }

    /// <summary>
    ///     This action updates a peer's display name and notes as an RFC 7396 merge patch; a field
    ///     that is absent or null is left unchanged. The other fields are fixed once a peer exists
    ///     (see <see cref="ClusterNodeWriteRequest" />). Returns 404 for an unknown id. Requires
    ///     <c>clusternodes:write</c>.
    /// </summary>
    [HttpPatch("{id:guid}")]
    [RequirePermission("clusternodes:write")]
    [SwaggerRequestExample(typeof(ClusterNodeWriteRequest), typeof(ClusterNodeWriteRequestExample))]
    public async Task<IActionResult> Patch(
        Guid id,
        ClusterNodeWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        ClusterNodeEntity node = await clusterNodeRepository.GetByIdAsync(id, stoppingToken);
        if (node is null)
            throw new NotFoundException("ClusterNode", id.ToString());

        if (request?.Name is not null)
            node.Name = request.Name;
        if (request?.Comments is not null)
            node.Comments = request.Comments;

        node.UpdatedAtUtc = DateTimeOffset.UtcNow;
        clusterNodeRepository.Update(node);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This action removes a peer from this node's registry only. The peer is not told and
    ///     keeps its own entry for this node, so a clean two-sided break means removing the
    ///     relationship on both nodes. The peer's channel TSIG key goes with it, in the same save,
    ///     so the peer can no longer authenticate; the key API refuses to delete a cluster key
    ///     any other way. A key of the same name that is not linked to this peer is left alone.
    ///     Returns 404 for an unknown id. Requires
    ///     <c>clusternodes:write</c>.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("clusternodes:write")]
    public async Task<IActionResult> RemoveNode(Guid id, CancellationToken stoppingToken)
    {
        ClusterNodeEntity node = await clusterNodeRepository.GetByIdAsync(id, stoppingToken);
        if (node is null)
            throw new NotFoundException("ClusterNode", id.ToString());

        TsigKeyEntity peerKey = await tsigKeyRepository.FindByNameAsync(
            node.TsigKeyName,
            stoppingToken
        );
        if (peerKey is not null && peerKey.ClusterNodeId == node.Id)
            tsigKeyRepository.Remove(peerKey);

        clusterNodeRepository.Remove(node);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This action creates a cluster invite and returns its code and expiry time (see
    ///     <see cref="ClusterInviteService" />). Requires <c>clusternodes:write</c>.
    /// </summary>
    [HttpPost("invite")]
    [RequirePermission("clusternodes:write")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ClusterInviteCreateResponseExample))]
    public async Task<ActionResult<ClusterInviteCreateResponse>> CreateInvite(
        CancellationToken stoppingToken
    )
    {
        string inviteCode = await inviteService.CreateInviteAsync(stoppingToken);

        return Ok(
            new ClusterInviteCreateResponse(
                inviteCode,
                DateTimeOffset.UtcNow + ClusterInviteService.DefaultValidity
            )
        );
    }

    /// <summary>
    ///     This action uses an invite code from another node to complete the join handshake with
    ///     that node, and returns the new peer (see <see cref="ClusterJoinService" />). Requires
    ///     <c>clusternodes:write</c>.
    /// </summary>
    [HttpPost("join")]
    [RequirePermission("clusternodes:write")]
    [SwaggerRequestExample(typeof(ClusterJoinRequest), typeof(ClusterJoinRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ClusterNodeDetailExample))]
    public async Task<ActionResult<ClusterNodeDetail>> Join(
        ClusterJoinRequest request,
        CancellationToken stoppingToken
    )
    {
        ClusterNodeEntity node = await joinService.JoinAsync(
            request?.InviteCode,
            request?.DisplayName,
            stoppingToken
        );

        return Ok(ToDetail(node));
    }

    /// <summary>
    ///     This action serves the cluster dashboard: a row with this node's query counts (last
    ///     hour, last 24 hours, all retained), then one row per known peer, collected live over the
    ///     TSIG-authenticated, TLS-encrypted cluster channel by
    ///     <see cref="IClusterQueryStatsAggregator" />. A peer that cannot be reached is still
    ///     listed, marked unreachable with null counts, so "no traffic" and "unreachable" look
    ///     different. With clustering disabled or no known peers, only this node's row is returned.
    ///     Requires <c>clusternodes:read</c>.
    /// </summary>
    [HttpGet("stats")]
    [RequirePermission("clusternodes:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ClusterAggregateQueryStatsListExample))]
    public async Task<ActionResult<IReadOnlyList<ClusterAggregateQueryStatsDetail>>> Stats(
        CancellationToken stoppingToken
    )
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        int selfQueriesLastHour = await queryLogRepository.CountAsync(
            new DnsQueryLogSearchCriteria(FromUtc: now - TimeSpan.FromHours(1)),
            stoppingToken
        );
        int selfQueriesLast24Hours = await queryLogRepository.CountAsync(
            new DnsQueryLogSearchCriteria(FromUtc: now - TimeSpan.FromHours(24)),
            stoppingToken
        );
        int selfTotalRetainedQueries = await queryLogRepository.CountAsync(
            new DnsQueryLogSearchCriteria(),
            stoppingToken
        );

        List<ClusterAggregateQueryStatsDetail> rows =
        [
            new ClusterAggregateQueryStatsDetail(
                null,
                "This node" + (clusterOptions.CurrentValue.Enable ? "" : " (clustering disabled)"),
                true,
                true,
                selfQueriesLastHour,
                selfQueriesLast24Hours,
                selfTotalRetainedQueries
            ),
        ];

        IReadOnlyList<ClusterPeerQueryStatsResult> peerResults =
            await queryStatsAggregator.CollectFromPeersAsync(stoppingToken);

        rows.AddRange(
            peerResults.Select(peer => new ClusterAggregateQueryStatsDetail(
                peer.ClusterNodeRegistryId,
                peer.PeerName,
                false,
                peer.Reachable,
                peer.Stats?.QueriesLastHour,
                peer.Stats?.QueriesLast24Hours,
                peer.Stats?.TotalRetainedQueries
            ))
        );

        return Ok(rows);
    }

    /// <summary>
    ///     This method converts a peer entity to its API representation.
    /// </summary>
    private static ClusterNodeDetail ToDetail(ClusterNodeEntity node) =>
        new(
            node.Id,
            node.Name,
            node.RemoteNodeId,
            node.Host,
            node.Port,
            node.TsigKeyName,
            node.TlsCertificateThumbprint,
            node.Status,
            node.LastHeartbeatAtUtc,
            node.MissedHeartbeatCount,
            node.Comments,
            node.CreatedAtUtc,
            node.UpdatedAtUtc
        );
}
