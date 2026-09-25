// Define our namespace.
namespace Ninlil.Dns.Server.Core.ClusterSync;

/// <summary>
///     This interface is the receiving half of cluster config sync, implemented once per synced
///     category (filter lists, filter list entries, access-control rules, rewrites, forwarders,
///     users, roles, settings sections). <c>ClusterChannelController</c>'s <c>config-sync</c>
///     action passes each inbound <see cref="ClusterConfigSyncEnvelope" /> to the applier whose
///     <see cref="EntityType" /> matches.
///     <para>
///         Each applier makes the last-write-wins decision itself with
///         <see cref="ClusterSyncConflictResolver.IncomingWins" />: it reads what it has locally
///         for <see cref="ClusterConfigSyncEnvelope.EntityKey" />, compares timestamps and node
///         IDs, and either writes the change through the same repositories a local REST write
///         uses or drops the stale envelope. Only the applier knows how to read the local copy,
///         so the controller does not take part.
///     </para>
/// </summary>
public interface IClusterConfigSyncApplier
{
    /// <summary>
    ///     This property contains the entity type this applier handles, compared with
    ///     <see cref="ClusterConfigSyncEnvelope.EntityType" /> case-sensitively.
    /// </summary>
    string EntityType { get; }

    /// <summary>
    ///     This method applies one inbound change if it wins against the local copy (see the
    ///     interface summary).
    /// </summary>
    /// <param name="envelope">The change to consider applying.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    Task ApplyAsync(ClusterConfigSyncEnvelope envelope, CancellationToken stoppingToken);
}
