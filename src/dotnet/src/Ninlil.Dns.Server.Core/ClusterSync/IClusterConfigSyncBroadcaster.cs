// Define our namespace.
namespace Ninlil.Dns.Server.Core.ClusterSync;

/// <summary>
///     This interface is the sending half of cluster config sync. The per-category event
///     subscribers call it with a snapshot of the changed entity and need to know nothing about
///     peers, TSIG or TLS pinning. The implementation is <c>ClusterConfigSyncBroadcaster</c> in
///     <c>Ninlil.Dns.Server.Cluster</c>, which owns the peer registry and the TSIG-signed, TLS
///     node-to-node channel. The interface lives here so the subscribers do not reference that
///     project.
/// </summary>
public interface IClusterConfigSyncBroadcaster
{
    /// <summary>
    ///     This method sends one change to every peer this node considers
    ///     <c>ClusterNodeStatusEnum.Healthy</c>, over the TSIG-signed, TLS cluster channel. It is
    ///     best effort: a peer that is unreachable or rejects the request is logged and skipped,
    ///     and no exception reaches the caller. A peer that misses a change is not sent it again
    ///     later. It returns at once when clustering is off or there are no peers.
    /// </summary>
    /// <param name="envelope">The change to broadcast.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    Task BroadcastAsync(ClusterConfigSyncEnvelope envelope, CancellationToken stoppingToken);
}
