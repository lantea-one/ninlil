// Define our namespace.
namespace Ninlil.Dns.Server.Core.ClusterSync;

/// <summary>
///     This record carries one node's own query-volume snapshot, read live from its own
///     persistent query log (<c>Server.QueryLog</c>) at the moment it was asked -- never cached,
///     never pushed proactively. It is deliberately shaped as three plain counts rather than
///     anything entity-specific, so <c>Ninlil.Dns.Server.Core</c> (which has zero dependency on
///     <c>Data.Abstractions</c>) can describe the wire shape without knowing anything about
///     <c>DnsQueryLogEntryEntity</c> itself.
/// </summary>
/// <param name="QueriesLastHour">How many queries this node has logged in the last hour.</param>
/// <param name="QueriesLast24Hours">How many queries this node has logged in the last 24 hours.</param>
/// <param name="TotalRetainedQueries">
///     How many query-log rows this node currently retains in total -- bounded by
///     <c>Server.QueryLog</c>'s own age-/count-based cleanup sweep, so this is "currently on
///     disk," not "ever served since this node was installed."
/// </param>
public sealed record ClusterQueryStatsSnapshot(
    int QueriesLastHour,
    int QueriesLast24Hours,
    int TotalRetainedQueries
);

/// <summary>
///     This record carries one peer's own aggregate query-stats result, as seen from the node
///     that asked for it -- either a real <see cref="Stats" /> snapshot, or
///     <see langword="null" /> when the peer could not be reached (or was not currently
///     <c>ClusterNodeStatusEnum.Healthy</c> to begin with). See
///     <see cref="IClusterQueryStatsAggregator" />'s own doc comment for why an unreachable peer
///     is always represented this way rather than omitted outright or treated as a failure.
/// </summary>
/// <param name="ClusterNodeRegistryId">This node's own local registry row id for the peer.</param>
/// <param name="PeerName">The peer's own operator-chosen display name, at the moment this was asked.</param>
/// <param name="Reachable">Whether this node was able to collect a live snapshot from the peer.</param>
/// <param name="Stats">The peer's own snapshot, or <see langword="null" /> when <see cref="Reachable" /> is <see langword="false" />.</param>
public sealed record ClusterPeerQueryStatsResult(
    Guid ClusterNodeRegistryId,
    string PeerName,
    bool Reachable,
    ClusterQueryStatsSnapshot Stats
);

/// <summary>
///     This interface is the cluster's own aggregate query-stats collector, the read-only
///     counterpart to <see cref="IClusterUsernameUniquenessChecker" />:
///     <c>Ninlil.Dns.Server.Cluster</c>'s own <c>ClusterQueryStatsAggregator</c> is the one real
///     implementation, reusing the cluster's own peer registry and TSIG-authenticated +
///     TLS-encrypted channel exactly the way every other cluster-channel caller already does --
///     declaring the abstraction here, in <c>Ninlil.Dns.Server.Core</c>, lets
///     <c>Ninlil.Web</c>'s own <c>ClusterController</c> depend on it without needing to know
///     anything about the cluster's own peer registry or signing mechanics.
///     <para>
///         Deliberately a live, on-demand fan-out rather than a warmed, continuously-updated
///         cache: unlike <c>DnsFilterEngine</c>'s own per-client-group cache (this project's usual
///         "define the abstraction, implement against an already-warm cache" shape), a cluster
///         dashboard is opened rarely and tolerates a second or two of latency far better than a
///         DNS query answer ever could -- there is no hot path here to protect, so the simpler
///         "ask every peer right now" design was chosen over building and invalidating a second
///         cache purely for this one page.
///     </para>
/// </summary>
public interface IClusterQueryStatsAggregator
{
    /// <summary>
    ///     This method asks every peer currently in this node's own registry for its own live
    ///     query-stats snapshot, in parallel. A peer that is not currently
    ///     <c>ClusterNodeStatusEnum.Healthy</c> is reported unreachable immediately, with no
    ///     network call at all -- its heartbeat status already answers that question, so a
    ///     dashboard load never waits out a timeout against a peer already known to be down. A
    ///     peer that <em>is</em> currently healthy but fails to answer within a short timeout is
    ///     reported unreachable the same way. A no-op, cheaply returning an empty list, when
    ///     clustering itself is disabled or no peer is currently known.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    Task<IReadOnlyList<ClusterPeerQueryStatsResult>> CollectFromPeersAsync(
        CancellationToken stoppingToken = default
    );
}
