// Define our namespace.
namespace Ninlil.Dns.Server.Core.ClusterSync;

/// <summary>
///     This record carries one node's query counts, read from its query log
///     (<c>Server.QueryLog</c>) when asked; nothing is cached or pushed. Plain counts keep
///     <c>Server.Core</c> free of storage types.
/// </summary>
/// <param name="QueriesLastHour">How many queries this node has logged in the last hour.</param>
/// <param name="QueriesLast24Hours">How many queries this node has logged in the last 24 hours.</param>
/// <param name="TotalRetainedQueries">
///     How many query-log rows this node holds now. The query-log cleanup limits it, so it is not
///     a lifetime total.
/// </param>
public sealed record ClusterQueryStatsSnapshot(
    int QueriesLastHour,
    int QueriesLast24Hours,
    int TotalRetainedQueries
);

/// <summary>
///     This record carries one peer's query stats as seen by the node that asked: a
///     <see cref="Stats" /> snapshot, or <see langword="null" /> when the peer was not healthy or
///     did not answer. Unreachable peers are listed rather than left out, so the dashboard can
///     show them.
/// </summary>
/// <param name="ClusterNodeRegistryId">This node's registry row ID for the peer.</param>
/// <param name="PeerName">The peer's display name at the time of asking.</param>
/// <param name="Reachable">Whether a snapshot was collected from the peer.</param>
/// <param name="Stats">The peer's snapshot, or <see langword="null" /> when <see cref="Reachable" /> is <see langword="false" />.</param>
public sealed record ClusterPeerQueryStatsResult(
    Guid ClusterNodeRegistryId,
    string PeerName,
    bool Reachable,
    ClusterQueryStatsSnapshot Stats
);

/// <summary>
///     This interface collects query stats from every cluster peer for the cluster dashboard
///     (<c>ClusterController</c> in <c>Ninlil.Web</c>). The implementation is
///     <c>ClusterQueryStatsAggregator</c> in <c>Ninlil.Dns.Server.Cluster</c>, which uses the peer
///     registry and the TSIG-signed, TLS cluster channel; the interface lives here so the
///     controller needs no knowledge of either.
///     <para>
///         It asks every peer each time rather than keeping a cache: the dashboard is opened
///         rarely and can wait a second or two, so a cache would add invalidation work for no
///         gain.
///     </para>
/// </summary>
public interface IClusterQueryStatsAggregator
{
    /// <summary>
    ///     This method asks every peer in the registry for its snapshot, in parallel. A peer that
    ///     is not <c>ClusterNodeStatusEnum.Healthy</c> is reported unreachable without a network
    ///     call, so the dashboard never waits on a peer already known to be down. A healthy peer
    ///     that does not answer within a short timeout is also reported unreachable. It returns an
    ///     empty list when clustering is off or there are no peers.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    Task<IReadOnlyList<ClusterPeerQueryStatsResult>> CollectFromPeersAsync(
        CancellationToken stoppingToken = default
    );
}
