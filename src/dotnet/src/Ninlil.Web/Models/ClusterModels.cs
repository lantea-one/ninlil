using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries a peer node's own metadata, for list and single-item responses
///     (<c>GET /api/cluster/nodes</c>). Its own TSIG key's shared secret is never included --
///     <see cref="TsigKeyName" /> is a soft reference only, the same convention
///     <c>TsigKeyDetail</c> itself follows for every other secret-bearing entity type.
/// </summary>
public record ClusterNodeDetail(
    Guid Id,
    string Name,
    Guid RemoteNodeId,
    string Host,
    int Port,
    string TsigKeyName,
    string TlsCertificateThumbprint,
    ClusterNodeStatusEnum Status,
    DateTimeOffset? LastHeartbeatAtUtc,
    int MissedHeartbeatCount,
    string Comments,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc
);

/// <summary>
///     This class carries the fields an operator may edit on an existing peer row
///     (<c>PATCH /api/cluster/nodes/{id}</c>) -- only its own display name and free-form notes.
///     Every other field (address, TSIG key, pinned certificate) is fixed by the join handshake
///     that created the row and cannot be edited afterward; changing any of those out from under
///     an already-trusted peer relationship would silently break it.
/// </summary>
/// <param name="Name">The peer's operator-chosen display name.</param>
/// <param name="Comments">Free-form operator notes about the peer.</param>
public record ClusterNodeWriteRequest(string Name = null, string Comments = null);

/// <summary>
///     This class carries a freshly created invite's own opaque code and expiry
///     (<c>POST /api/cluster/nodes/invite</c>). The code itself is this project's one deliberate
///     exception to its usual "never return a secret except at creation time, and never as plain
///     text" posture beyond that single reveal -- see <c>ClusterInviteCode</c>'s own doc comment.
/// </summary>
/// <param name="InviteCode">The opaque code to hand to the joining node's own operator.</param>
/// <param name="ExpiresAtUtc">The UTC moment after which this code can no longer be consumed.</param>
public record ClusterInviteCreateResponse(string InviteCode, DateTimeOffset ExpiresAtUtc);

/// <summary>
///     This class carries the fields used to consume an invite code and join a cluster
///     (<c>POST /api/cluster/nodes/join</c>).
/// </summary>
/// <param name="InviteCode">The opaque code copied from the inviting node's own Angular "Cluster" page.</param>
/// <param name="DisplayName">
///     An optional display name for the inviter's own new row in this node's registry --
///     falls back to a name derived from the inviter's node identifier when omitted.
/// </param>
public record ClusterJoinRequest(string InviteCode, string DisplayName = null);

/// <summary>
///     This class carries one node's own aggregate query-stats row for the cluster dashboard
///     (<c>GET /api/cluster/nodes/stats</c>) -- either this node itself
///     (<see cref="IsSelf" /> <see langword="true" />, always <see cref="Reachable" />) or one
///     known peer, reported unreachable rather than omitted when its own live snapshot could not
///     be collected. See <see cref="Ninlil.Dns.Server.Core.ClusterSync.IClusterQueryStatsAggregator" />'s
///     own doc comment for why an unreachable peer is never simply left out of the list.
/// </summary>
/// <param name="ClusterNodeRegistryId">
///     This node's own local registry row id for the peer, or <see langword="null" /> for
///     <see cref="IsSelf" />'s own row (this node has no registry entry for itself).
/// </param>
/// <param name="NodeName">The node's own display name.</param>
/// <param name="IsSelf">Whether this row describes the node answering the request itself.</param>
/// <param name="Reachable">Whether a live snapshot was actually collected for this row.</param>
/// <param name="QueriesLastHour">How many queries this node logged in the last hour, or <see langword="null" /> when unreachable.</param>
/// <param name="QueriesLast24Hours">How many queries this node logged in the last 24 hours, or <see langword="null" /> when unreachable.</param>
/// <param name="TotalRetainedQueries">How many query-log rows this node currently retains, or <see langword="null" /> when unreachable.</param>
public record ClusterAggregateQueryStatsDetail(
    Guid? ClusterNodeRegistryId,
    string NodeName,
    bool IsSelf,
    bool Reachable,
    int? QueriesLastHour,
    int? QueriesLast24Hours,
    int? TotalRetainedQueries
);
