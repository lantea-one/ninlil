// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class holds the last-write-wins bookkeeping that cluster config sync needs for a
///     synced <em>settings</em> section, one row per section name (for example
///     <c>"EventBroker"</c>, <c>"RateLimiting"</c>, <c>"DnsCookie"</c>, <c>"ReverseProxy"</c>).
///     <para>
///         The other synced categories (filter lists, access-control rules, rewrites,
///         forwarders) are <see cref="AuditableEntityBase" /> rows, whose
///         <see cref="AuditableEntityBase.UpdatedAtUtc" /> and
///         <see cref="AuditableEntityBase.OriginatingNodeId" /> already give last-write-wins what
///         it needs. A settings section has no row: its values live in the runtime JSON
///         configuration file (see <c>PresetConfigurationFileApplier</c> and
///         <c>RuntimeConfigurationOptions</c>), not the database. This table holds only the
///         conflict-resolution fields for each section, never the values, so there is no second
///         copy of the configuration to keep in step with the file.
///     </para>
/// </summary>
public class ClusterSyncedSettingsStateEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains the settings section name: the <c>SectionName</c> constant of the
    ///     options class (for example <c>DnsServerEventPublisherOptions.SectionName</c>). It is
    ///     unique in this table.
    /// </summary>
    public string SectionName { get; set; }
}
