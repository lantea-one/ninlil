// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class extends <see cref="EntityBase" /> for entities that change after they are
///     created: records are edited, a user's profile is updated, a lease moves from offered to
///     active. Append-only records, such as log entries or metric samples, derive from
///     <see cref="EntityBase" /> directly.
/// </summary>
public abstract class AuditableEntityBase : EntityBase
{
    /// <summary>
    ///     This property contains the UTC date and time at which the entity was last modified, or
    ///     <see langword="null" /> if it has never been modified since it was created.
    /// </summary>
    public DateTimeOffset? UpdatedAtUtc { get; set; }

    /// <summary>
    ///     This property contains the identifier of the cluster node (<c>ClusterOptions.NodeId</c> in
    ///     <c>Ninlil.Dns.Server.Cluster</c>) that last created or modified this entity locally. It is
    ///     <see langword="null" /> on a single node, or for entity types the cluster does not sync.
    ///     It lives on the base class rather than on each synced type (<c>DnsFilterListEntity</c>,
    ///     <c>DnsFilterListEntryEntity</c>, <c>DnsAccessControlRuleEntity</c>, <c>DnsRewriteEntity</c>,
    ///     <c>DnsForwarderEntity</c>), so a new synced entity needs no schema change.
    ///     <para>
    ///         Together with the last-modified time (<see cref="UpdatedAtUtc" />, or
    ///         <see cref="EntityBase.CreatedAtUtc" /> if never updated; see
    ///         <see cref="AuditableEntityBaseExtensions.GetLastModifiedUtc" />) it is all the state
    ///         last-write-wins conflict resolution needs: when, and which node. On equal timestamps,
    ///         <c>ClusterSyncConflictResolver.IncomingWins</c> breaks the tie by comparing node
    ///         identifiers as strings.
    ///     </para>
    /// </summary>
    public Guid? OriginatingNodeId { get; set; }
}

/// <summary>
///     This class holds the derived value cluster config-sync conflict resolution needs from an
///     <see cref="AuditableEntityBase" />: the time it was last changed.
/// </summary>
public static class AuditableEntityBaseExtensions
{
    /// <summary>
    ///     This method returns the time an entity was last modified: its
    ///     <see cref="AuditableEntityBase.UpdatedAtUtc" /> if it was ever updated, otherwise its
    ///     <see cref="EntityBase.CreatedAtUtc" />. The last-write-wins comparison
    ///     (<c>ClusterSyncConflictResolver.IncomingWins</c>) uses it on both sides of a sync.
    /// </summary>
    public static DateTimeOffset GetLastModifiedUtc(this AuditableEntityBase entity) =>
        entity.UpdatedAtUtc ?? entity.CreatedAtUtc;
}
