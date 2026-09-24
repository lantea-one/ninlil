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
///     This controller administers this node's own cluster membership: listing, renaming, and
///     removing known peers, and running both halves of the invite-code join handshake (see
///     <see cref="ClusterInviteService" />/<see cref="ClusterJoinService" /> for the real
///     handshake logic this controller is thin sugar over). This is the human-operator-facing
///     surface -- JWT-authenticated like every other administrative controller in this project --
///     distinct from <see cref="ClusterChannelController" />, the peer-to-peer, TSIG-authenticated
///     surface a joined peer's own background services call. Every action requires the
///     <c>clusternodes:read</c> or <c>clusternodes:write</c> permission.
/// </summary>
/// <param name="clusterNodeRepository">The repository this controller reads and writes peer rows through.</param>
/// <param name="unitOfWork">The unit of work every staged change is committed through.</param>
/// <param name="inviteService">The service this controller delegates invite creation to.</param>
/// <param name="joinService">The service this controller delegates join-handshake consumption to.</param>
/// <param name="queryLogRepository">
///     Read directly by <see cref="Stats" /> to build this node's own row in the aggregate
///     query-stats response -- see that action's own doc comment.
/// </param>
/// <param name="queryStatsAggregator">
///     The collector <see cref="Stats" /> delegates every peer's own row to -- see
///     <see cref="IClusterQueryStatsAggregator" />'s own doc comment.
/// </param>
/// <param name="clusterOptions">This node's own cluster configuration, read for <see cref="Stats" />'s own node identity.</param>
[ApiController]
[Route("api/cluster/nodes")]
public class ClusterController(
    IClusterNodeRepository clusterNodeRepository,
    IUnitOfWork unitOfWork,
    ClusterInviteService inviteService,
    ClusterJoinService joinService,
    IDnsQueryLogRepository queryLogRepository,
    IClusterQueryStatsAggregator queryStatsAggregator,
    IOptionsMonitor<ClusterOptions> clusterOptions
) : ControllerBase
{
    /// <summary>
    ///     This action lists every peer node currently in this node's own registry.
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

        // Both the page itself and its total count are pushed down to the storage engine -- a
        // real, translated ORDER BY/OFFSET/LIMIT (or the MongoDB equivalent) -- rather than
        // materializing every peer into memory first.
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
    ///     This action looks up a single peer node by its own local registry row identifier.
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
    ///     This action partially updates a peer's own display name and/or notes via RFC 7396 JSON
    ///     Merge Patch -- see <see cref="ClusterNodeWriteRequest" /> for why every other field is
    ///     fixed once a peer row exists.
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
    ///     This action removes a peer node from this node's own registry -- purely local: it does
    ///     not notify the peer, and does not affect the peer's own registry entry for this node
    ///     (an operator who wants a clean, two-sided break must remove the relationship from both
    ///     nodes' own "Cluster" pages, a deliberate "no cross-node cleanup" posture documented as
    ///     an accepted limitation).
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("clusternodes:write")]
    public async Task<IActionResult> RemoveNode(Guid id, CancellationToken stoppingToken)
    {
        ClusterNodeEntity node = await clusterNodeRepository.GetByIdAsync(id, stoppingToken);
        if (node is null)
            throw new NotFoundException("ClusterNode", id.ToString());

        clusterNodeRepository.Remove(node);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This action creates a fresh cluster invite -- see <see cref="ClusterInviteService" />
    ///     for the real generation logic this delegates to.
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
    ///     This action consumes an invite code, completing the join handshake against the
    ///     inviting node -- see <see cref="ClusterJoinService" /> for the real handshake logic
    ///     this delegates to.
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
    ///     This action serves the aggregate cluster dashboard: this node's own live
    ///     query-stats snapshot, plus one row per known peer, each peer's own row collected live
    ///     over the existing TSIG-authenticated + TLS-encrypted cluster channel via
    ///     <see cref="IClusterQueryStatsAggregator" />. A peer that could not be reached is still
    ///     included in the response, marked unreachable with every count <see langword="null" /> --
    ///     never silently omitted, so an operator can tell "this peer has no query traffic" apart
    ///     from "this node could not reach this peer right now." Returns an empty peer list (this
    ///     node's own row only) when clustering is disabled or no peer is currently known.
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
    ///     This method converts a peer node entity into its REST representation.
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
