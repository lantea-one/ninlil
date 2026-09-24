// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.CatalogSync;

/// <summary>
///     This class binds to the <c>CatalogSync</c> section of the application's configuration and
///     controls <see cref="CatalogClusterAddressSyncService" />'s own polling cadence -- the
///     periodic pass that keeps every catalog-managed zone's own
///     <c>ZoneEntity.NotifyAddresses</c>/<c>PrimaryServerAddresses</c> current as cluster peers
///     join, leave, or change health, without an operator ever touching either list by hand.
/// </summary>
public class CatalogSyncOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "CatalogSync";

    /// <summary>
    ///     This property contains how often <see cref="CatalogClusterAddressSyncService" /> runs
    ///     its reconciliation pass. Defaults to one minute -- frequent enough that a peer's health
    ///     transition (a fresh join, or <c>ClusterHeartbeatBackgroundService</c> marking one
    ///     <c>Unreachable</c>) is reflected in every catalog-managed zone's own address list well
    ///     within a homelab operator's own patience, without adding meaningful database load for a
    ///     pass this project's own typical 2-to-5-node, low-zone-count deployment target makes
    ///     genuinely cheap.
    /// </summary>
    public TimeSpan AddressReconcileInterval { get; set; } = TimeSpan.FromMinutes(1);
}
