// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class holds the last-write-wins bookkeeping the cluster's own config-sync mechanism
///     needs for a synced <em>settings</em> section -- one row per section name (for example
///     <c>"EventBroker"</c>, <c>"RateLimiting"</c>, <c>"DnsCookie"</c>, <c>"ReverseProxy"</c>).
///     <para>
///         Every other category the cluster syncs (filter lists, access-control rules,
///         rewrites, forwarders) is a real, already-persisted <see cref="AuditableEntityBase" />-
///         derived row, so its own <see cref="AuditableEntityBase.UpdatedAtUtc" />/
///         <see cref="AuditableEntityBase.OriginatingNodeId" /> already carry exactly the state a
///         last-write-wins comparison needs -- no new table required. A settings section has no
///         such row: its actual values live in the runtime JSON configuration file on disk
///         (written and read via <c>PresetConfigurationFileApplier</c>/
///         <c>RuntimeConfigurationOptions</c>), not in the database at all, so there is nowhere to
///         hang those two fields directly. This entity is that "wherever" -- a small, dedicated
///         table carrying only the conflict-resolution bookkeeping for a settings section, never
///         the settings values themselves (which stay exactly where every other settings
///         controller already keeps them, avoiding a second, parallel, potentially-drifting copy
///         of configuration this project would then have to keep consistent with the real file).
///     </para>
/// </summary>
public class ClusterSyncedSettingsStateEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains the settings section's own stable name -- the same
    ///     <c>SectionName</c> constant each settings options class already declares (for example
    ///     <c>DnsServerEventPublisherOptions.SectionName</c>), unique across every row in this
    ///     table.
    /// </summary>
    public string SectionName { get; set; }
}
