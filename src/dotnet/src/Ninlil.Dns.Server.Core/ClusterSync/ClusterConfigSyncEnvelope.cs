// Define our namespace.
namespace Ninlil.Dns.Server.Core.ClusterSync;

/// <summary>
///     This record is the cluster's own one generic wire shape for a single operational-config change
///     propagated cluster-wide: an entity type, an entity-scoped key, its new content (when the
///     change was not a deletion), and the last-write-wins conflict-resolution state (a UTC
///     timestamp plus the id of the node the change originated on).
///     <para>
///         Deliberately one shape for every synced category (filter lists, filter list entries,
///         access-control rules, rewrites, forwarders, settings sections) rather than five
///         bespoke wire formats: every
///         category's own change ultimately reduces to exactly this same tuple, and a single
///         shape means <c>ClusterChannelController</c>'s own inbound endpoint, and every outbound
///         broadcaster call, needs writing (and testing) only once.
///     </para>
///     <para>
///         <see cref="PayloadJson" /> is deliberately an already-serialized string, not a generic
///         type parameter -- this record (and the interfaces alongside it) lives in
///         <c>Ninlil.Dns.Server.Core</c>, which has no dependency on
///         <c>Ninlil.Dns.Data.Abstractions</c> at all, so it cannot know the shape of a <c>DnsFilterListEntity</c> or any
///         other entity it carries. Each <see cref="IClusterConfigSyncApplier" /> implementation
///         (which does have that dependency) is the only place that ever deserializes it.
///     </para>
/// </summary>
/// <param name="EntityType">
///     Which kind of thing this envelope carries -- one of the stable, per-category string
///     constants each <see cref="IClusterConfigSyncApplier" /> implementation declares as its own
///     <see cref="IClusterConfigSyncApplier.EntityType" /> (for example <c>"FilterList"</c>,
///     "AccessControlRule", or <c>"Settings"</c>).
/// </param>
/// <param name="EntityKey">
///     The specific entity's own stable key within <see cref="EntityType" /> -- a
///     <see cref="Guid" />'s string form for every entity-row category this sub-phase syncs, or a
///     settings section's own stable name (for example <c>"EventBroker"</c>) for
///     <c>EntityType == "Settings"</c>, where there is no single row identifier at all.
/// </param>
/// <param name="IsDeleted">
///     Whether this change represents a deletion. When <see langword="true" />,
///     <see cref="PayloadJson" /> is <see langword="null" /> -- there is nothing left to describe
///     beyond "this no longer exists as of <see cref="LastModifiedUtc" />."
/// </param>
/// <param name="PayloadJson">
///     The entity's (or settings section's) new content, serialized as JSON in whatever shape
///     the matching <see cref="IClusterConfigSyncApplier" /> expects -- <see langword="null" />
///     when <see cref="IsDeleted" /> is <see langword="true" />.
/// </param>
/// <param name="LastModifiedUtc">
///     The UTC instant this change actually happened, at the originating node -- the value every
///     last-write-wins comparison (<see cref="ClusterSyncConflictResolver.IncomingWins" />)
///     actually compares. Always <c>AuditableEntityBaseExtensions.GetLastModifiedUtc</c>'s own
///     precision: whatever a plain <see cref="DateTimeOffset.UtcNow" /> read on the originating
///     node returned at write time, the same precision (and the same lack of any cluster-wide
///     clock synchronization assumption) as every other <c>*AtUtc</c> field already stamped
///     throughout this project.
/// </param>
/// <param name="OriginatingNodeId">
///     The cluster node (<c>ClusterOptions.NodeId</c>) the change actually happened on -- carried
///     through unchanged by every peer this change is ever re-synced from, and the deterministic
///     tie-breaker <see cref="ClusterSyncConflictResolver.IncomingWins" /> falls back to when two
///     nodes recorded the exact same <see cref="LastModifiedUtc" />.
/// </param>
public sealed record ClusterConfigSyncEnvelope(
    string EntityType,
    string EntityKey,
    bool IsDeleted,
    string PayloadJson,
    DateTimeOffset LastModifiedUtc,
    Guid OriginatingNodeId
);
