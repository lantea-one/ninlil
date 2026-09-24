// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class extends <see cref="EntityBase" /> for entities that are expected to change
///     after they are first created -- a zone's records get edited, a user's profile gets
///     updated, a lease moves from offered to active. Purely append-only records, such as a log
///     entry or a metric sample, have no need for this and derive from <see cref="EntityBase" />
///     directly instead.
/// </summary>
public abstract class AuditableEntityBase : EntityBase
{
    /// <summary>
    ///     This property contains the UTC date and time at which the entity was last modified, or
    ///     <see langword="null" /> if it has never been modified since it was created.
    /// </summary>
    public DateTimeOffset? UpdatedAtUtc { get; set; }

    /// <summary>
    ///     This property contains the identifier of the cluster node (<c>ClusterOptions.NodeId</c>,
    ///     in <c>Ninlil.Dns.Server.Cluster</c>) that most recently created or modified this entity
    ///     <em>locally</em> -- <see langword="null" /> on a single-node deployment, or for an
    ///     entity type this project does not (yet) propagate cluster-wide. Deliberately declared
    ///     once here, on the shared base every auditable entity already derives from, rather than
    ///     bolted onto each of the handful of entity types the cluster's own operational config-sync
    ///     mechanism actually uses it for (<c>DnsFilterListEntity</c>/<c>DnsFilterListEntryEntity</c>/
    ///     <c>DnsAccessControlRuleEntity</c>/<c>DnsRewriteEntity</c>/<c>DnsForwarderEntity</c>) --
    ///     the same "design the shared shape once" reasoning that already put
    ///     <see cref="UpdatedAtUtc" /> here instead of on each individual entity, and one that lets
    ///     a future cluster-synced entity opt in for free just by deriving from this class, with no
    ///     further schema change of its own.
    ///     <para>
    ///         Combined with <see cref="UpdatedAtUtc" /> (falling back to <see cref="EntityBase.CreatedAtUtc" />
    ///         when an entity has never been updated -- see <see cref="AuditableEntityBaseExtensions.GetLastModifiedUtc" />),
    ///         this is the complete last-write-wins conflict-resolution state the cluster
    ///         config-sync mechanism needs: a moment in time, plus who to credit (or blame)
    ///         for it. It is never itself compared against another node's clock -- only the
    ///         accompanying timestamp is -- so no cluster-wide clock synchronization is assumed
    ///         anywhere in this project; a tied timestamp is broken by comparing this value's own
    ///         string form ordinally (see <c>ClusterSyncConflictResolver.IncomingWins</c>), not by
    ///         trusting either node's clock any further than the timestamp already does.
    ///     </para>
    /// </summary>
    public Guid? OriginatingNodeId { get; set; }
}

/// <summary>
///     This class holds the one small piece of derived logic every cluster-config-sync conflict
///     resolution needs from an <see cref="AuditableEntityBase" />-derived entity: a single,
///     unambiguous "when was this last really changed" instant.
/// </summary>
public static class AuditableEntityBaseExtensions
{
    /// <summary>
    ///     This method returns the instant an entity was last modified -- its own
    ///     <see cref="AuditableEntityBase.UpdatedAtUtc" /> if it has ever been updated, or its
    ///     <see cref="EntityBase.CreatedAtUtc" /> otherwise. This is the one value the cluster's
    ///     last-write-wins comparison (<c>ClusterSyncConflictResolver.IncomingWins</c>) treats as
    ///     an entity's "current" timestamp, on both the sending and receiving side of a sync.
    /// </summary>
    public static DateTimeOffset GetLastModifiedUtc(this AuditableEntityBase entity) =>
        entity.UpdatedAtUtc ?? entity.CreatedAtUtc;
}
