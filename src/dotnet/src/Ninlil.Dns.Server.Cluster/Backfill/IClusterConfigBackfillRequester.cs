// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Backfill;

/// <summary>
///     This interface is the recording side of the config backfill: it marks a peer as owed a
///     backfill and nothing else. It is separate from <see cref="IClusterConfigBackfillRunner" />
///     because the runner sends through <see cref="Channel.ClusterConfigSyncBroadcaster" />, and
///     the broadcaster must record a request when a live delivery fails. The broadcaster depends
///     on this interface, which depends on neither, so the container has no cycle.
/// </summary>
public interface IClusterConfigBackfillRequester
{
    /// <summary>
    ///     This method records that a peer is owed a backfill, which the next heartbeat that
    ///     reaches the peer then runs. A peer that is unknown or still <c>Joining</c> gets no
    ///     request: the joining node is the one that receives a backfill, never the one that
    ///     sends it.
    /// </summary>
    /// <param name="clusterNodeId">The identifier of the peer's local row.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns><see langword="true" /> when the request is recorded.</returns>
    Task<bool> RequestAsync(Guid clusterNodeId, CancellationToken stoppingToken = default);

    /// <summary>
    ///     This method tells the requester that a pass to a peer is about to read the peer's
    ///     request, so a later <see cref="RequestAsync" /> must reach the database again.
    /// </summary>
    /// <param name="clusterNodeId">The identifier of the peer's local row.</param>
    void NotePassStarted(Guid clusterNodeId);
}
