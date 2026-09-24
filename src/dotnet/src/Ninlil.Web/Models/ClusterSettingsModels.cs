// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries this node's own currently-effective cluster settings
///     (<c>GET /api/cluster-settings</c>). <see cref="NodeId" /> is read-only, included for an
///     operator's own diagnostic reference -- it is never writable through
///     <c>ClusterSettingsController.Patch</c>, per <c>ClusterOptions.NodeId</c>'s own doc comment.
///     <see cref="AddressReconcileInterval" /> is a genuinely distinct, cluster-adjacent options
///     class -- <c>CatalogSyncOptions</c>, not <c>ClusterOptions</c> itself -- folded into this
///     same settings surface rather than given a controller/page of its own, since it exists
///     purely to keep a catalog-managed zone's own NOTIFY/primary address lists reconciled
///     against this node's own live cluster membership; see
///     <c>CatalogClusterAddressSyncService</c>'s own doc comment.
/// </summary>
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
