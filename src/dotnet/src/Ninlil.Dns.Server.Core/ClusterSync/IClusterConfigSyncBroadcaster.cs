// Define our namespace.
namespace Ninlil.Dns.Server.Core.ClusterSync;

/// <summary>
///     This interface is the sending half of the cluster's own config sync -- the abstraction
///     every per-category event subscriber (one per synced entity type) calls once it has built
///     the entity's own current snapshot, so it never needs to know anything about the cluster's
///     own peer registry, TSIG signing, or TLS pinning underneath. <c>Ninlil.Dns.Server.Cluster</c>'s
///     own <c>ClusterConfigSyncBroadcaster</c> is the one real implementation, since only that
///     project already holds the peer registry and the TSIG-authenticated + TLS-encrypted
///     node-to-node channel <c>Ninlil.Dns.Server.Cluster</c> built -- declaring the interface here, in
///     <c>Ninlil.Dns.Server.Core</c>, instead of there, is exactly what lets every subscriber
///     (which lives in a project that has no reason to reference <c>Ninlil.Dns.Server.Cluster</c>
///     directly) depend on the abstraction alone.
/// </summary>
public interface IClusterConfigSyncBroadcaster
{
    /// <summary>
    ///     This method sends one operational-config change to every peer this node currently
    ///     considers <c>ClusterNodeStatusEnum.Healthy</c>, over the existing TSIG-authenticated +
    ///     TLS-encrypted cluster channel. A best-effort, fire-and-forget broadcast: a peer that is
    ///     unreachable, or that rejects the request, is logged and skipped -- it never blocks or
    ///     fails delivery to any other peer, and never propagates back to the caller as an
    ///     exception (see the real implementation's own doc comment for why a peer that missed a
    ///     change while briefly unreachable is a real, deliberately accepted limitation of this
    ///     sub-phase, not silently patched over here). A no-op, cheaply, when clustering itself is
    ///     disabled or no peer is currently known.
    /// </summary>
    /// <param name="envelope">The change to broadcast.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    Task BroadcastAsync(ClusterConfigSyncEnvelope envelope, CancellationToken stoppingToken);
}
