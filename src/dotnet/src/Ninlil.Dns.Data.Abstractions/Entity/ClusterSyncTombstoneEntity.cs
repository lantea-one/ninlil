// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class records that a synced entity was deleted, one row per entity type and key, so
///     cluster config sync can tell "deleted" apart from "never seen". Without it, a create or
///     update that was sent before a delete but arrives after it (deliveries are concurrent and
///     unordered) would bring the row back. The deletion time and node are kept the same way as on
///     any synced row: <see cref="EntityBase.CreatedAtUtc" /> or
///     <see cref="AuditableEntityBase.UpdatedAtUtc" />, and
///     <see cref="AuditableEntityBase.OriginatingNodeId" />, so last-write-wins compares an
///     incoming change against the deletion with the usual rule.
///     <para>
///         Tombstones are written by the config-sync appliers (an incoming delete) and the sending
///         subscribers (a local delete while clustering is on), and removed when a newer create of
///         the same key wins. They are kept indefinitely: there is one small row per deletion, and
///         a peer that has been away for a long time can still send a stale change. They are
///         sync bookkeeping, not configuration, so a backup does not include them.
///     </para>
/// </summary>
public class ClusterSyncTombstoneEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains the synced entity type the deleted entity belonged to, as in
    ///     <c>ClusterConfigSyncEnvelope.EntityType</c> (for example <c>"User"</c>).
    /// </summary>
    public string EntityType { get; set; }

    /// <summary>
    ///     This property contains the deleted entity's key, as in
    ///     <c>ClusterConfigSyncEnvelope.EntityKey</c> (the entity's id as a string).
    /// </summary>
    public string EntityKey { get; set; }
}
