// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Backfill;

/// <summary>
///     This interface is how the rest of the cluster code starts and requests a config backfill:
///     a push of every synced item this node holds to one peer. The heartbeat starts a pass after
///     a successful probe of a peer that is owed one, and the join service records the request
///     directly. Anything else that finds a peer has missed config (a live delivery that failed)
///     calls <see cref="RequestBackfillAsync" /> and the next successful heartbeat does the rest.
/// </summary>
public interface IClusterConfigBackfillRunner
{
    /// <summary>
    ///     This method starts a backfill pass to one peer in the background, unless a pass to
    ///     that peer is already running. It does not check that the peer is owed a backfill; the
    ///     pass finds that out and ends at once when the peer is not.
    /// </summary>
    /// <param name="clusterNodeId">The identifier of the peer's local row.</param>
    /// <returns><see langword="true" /> when a pass started, <see langword="false" /> when one is already running.</returns>
    bool TryStart(Guid clusterNodeId);

    /// <summary>
    ///     This method records that a peer is owed a backfill, which the next heartbeat that
    ///     reaches the peer then runs. A peer that already has a request gets its request time
    ///     moved to a time later than any before it, so a pass that is running keeps the request and one
    ///     more pass follows. Repeated requests while no pass has read the row cost no database
    ///     access (see <see cref="IClusterConfigBackfillRequester" />). A peer
    ///     that is unknown or still <c>Joining</c> gets no request: the joining node is the one
    ///     that receives a backfill, never the one that sends it.
    /// </summary>
    /// <param name="clusterNodeId">The identifier of the peer's local row.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns><see langword="true" /> when the request is recorded.</returns>
    Task<bool> RequestBackfillAsync(Guid clusterNodeId, CancellationToken stoppingToken = default);
}
