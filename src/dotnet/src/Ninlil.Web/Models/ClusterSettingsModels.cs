// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries this node's cluster settings (<c>GET /api/cluster-settings</c>), from
///     <c>ClusterOptions</c>. <see cref="NodeId" /> is read-only and shown for diagnostics;
///     <c>ClusterSettingsController.Patch</c> cannot change it. <see cref="AddressReconcileInterval" />
///     comes from <c>CatalogSyncOptions</c> instead. It shares this page because it only exists to
///     keep the NOTIFY and primary address lists of catalog-managed zones in step with cluster
///     membership (see <c>CatalogClusterAddressSyncService</c>).
/// </summary>
/// <param name="Enable">Whether clustering is on. Defaults to <see langword="false" />.</param>
/// <param name="NodeId">This node's identifier, generated at first startup.</param>
/// <param name="AdvertisedHost">The host name or address peers use to reach this node. Empty by default, which makes invites fail until it is set.</param>
/// <param name="AdvertisedPort">The TCP port peers use to reach this node. Defaults to 0, which must be changed before invites work.</param>
/// <param name="HeartbeatInterval">How often each peer is probed. Defaults to 30 seconds.</param>
/// <param name="HeartbeatTimeout">How long one probe may take. Defaults to 10 seconds.</param>
/// <param name="MissedHeartbeatThreshold">How many consecutive failed probes mark a peer unreachable. Defaults to 3.</param>
/// <param name="AddressReconcileInterval">How often catalog zone address lists are reconciled with cluster membership. Defaults to 1 minute.</param>
public record ClusterSettingsDetail(
    bool Enable,
    Guid NodeId,
    string AdvertisedHost,
    int AdvertisedPort,
    TimeSpan HeartbeatInterval,
    TimeSpan HeartbeatTimeout,
    int MissedHeartbeatThreshold,
    TimeSpan AddressReconcileInterval
);
