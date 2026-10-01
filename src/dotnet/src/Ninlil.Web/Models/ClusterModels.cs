using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries a peer node in this node's cluster registry, as returned by
///     <c>GET /api/cluster/nodes</c> and <c>GET /api/cluster/nodes/{id}</c>. The shared secret of
///     the peer's TSIG key is never included; <see cref="TsigKeyName" /> only names the key, as
///     <c>TsigKeyDetail</c> omits its secret.
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
///     (<c>PATCH /api/cluster/nodes/{id}</c>): only the display name and notes. The address, TSIG
///     key and pinned certificate are set by the join handshake and cannot be edited, because
///     changing them would break the trust between the two nodes.
/// </summary>
/// <param name="Name">The peer's operator-chosen display name, or <see langword="null" /> to keep it.</param>
/// <param name="Comments">Free-form operator notes about the peer, or <see langword="null" /> to keep them.</param>
public record ClusterNodeWriteRequest(string Name = null, string Comments = null);

/// <summary>
///     This class carries a new invite's opaque code and expiry
///     (<c>POST /api/cluster/nodes/invite</c>). The code contains the cluster's shared TSIG secret
///     in plaintext; this response is the one place that secret is revealed (see
///     <c>ClusterInviteCode</c>).
/// </summary>
/// <param name="InviteCode">The opaque code to hand to the operator of the joining node.</param>
/// <param name="ExpiresAtUtc">The UTC moment after which the code can no longer be used.</param>
public record ClusterInviteCreateResponse(string InviteCode, DateTimeOffset ExpiresAtUtc);

/// <summary>
///     This class carries the fields used to consume an invite code and join a cluster
///     (<c>POST /api/cluster/nodes/join</c>).
/// </summary>
/// <param name="InviteCode">The opaque code copied from the Cluster page of the inviting node.</param>
/// <param name="DisplayName">
///     An optional display name for the inviter's new row in this node's registry. When omitted,
///     a name derived from the inviter's node identifier is used.
/// </param>
public record ClusterJoinRequest(string InviteCode, string DisplayName = null);

/// <summary>
///     This class carries one node's query statistics for the cluster dashboard
///     (<c>GET /api/cluster/nodes/stats</c>). A row is either the answering node
///     (<see cref="IsSelf" /> is <see langword="true" /> and it is always <see cref="Reachable" />)
///     or a known peer. A peer whose snapshot could not be collected is reported unreachable
///     rather than left out (see
///     <see cref="Ninlil.Dns.Server.Core.ClusterSync.IClusterQueryStatsAggregator" />).
/// </summary>
/// <param name="ClusterNodeRegistryId">
///     The id of the peer's row in this node's registry, or <see langword="null" /> for the
///     answering node, which has no row for itself.
/// </param>
/// <param name="NodeName">The node's display name.</param>
/// <param name="IsSelf">Whether this row describes the node answering the request.</param>
/// <param name="Reachable">Whether a live snapshot was collected for this row.</param>
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
