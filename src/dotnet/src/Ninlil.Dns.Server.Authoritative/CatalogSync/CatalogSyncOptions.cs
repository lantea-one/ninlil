// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.CatalogSync;

/// <summary>
///     This class binds to the <c>CatalogSync</c> section of the application's configuration and
///     sets how often <see cref="CatalogClusterAddressSyncService" /> updates the
///     <c>ZoneEntity.NotifyAddresses</c> and <c>PrimaryServerAddresses</c> of catalog-managed zones as
///     cluster peers join, leave or change health.
/// </summary>
public class CatalogSyncOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "CatalogSync";

    /// <summary>
    ///     This property contains how often <see cref="CatalogClusterAddressSyncService" /> runs. One
    ///     minute by default: a peer's health change (a join, or <c>ClusterHeartbeatBackgroundService</c>
    ///     marking it <c>Unreachable</c>) shows up quickly, and the pass is cheap for a small cluster.
    /// </summary>
    public TimeSpan AddressReconcileInterval { get; set; } = TimeSpan.FromMinutes(1);
}
