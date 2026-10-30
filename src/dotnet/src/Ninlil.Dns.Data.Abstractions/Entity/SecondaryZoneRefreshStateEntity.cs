// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class records when a primary last confirmed or delivered this node's copy of a
///     secondary zone: the start of the Expire interval of RFC 1034 §4.3.5. It is node-local
///     runtime state with one row per zone, so it is not part of a backup and is not synced
///     between cluster nodes. The secondary refresh service writes it and reads it at start, so
///     a restart does not restart the Expire interval.
///     <para>
///         The row has no foreign key to <see cref="ZoneEntity" />, like
///         <see cref="ZoneChangeJournalEntryEntity" />, so deleting a zone does not touch it and
///         the refresh service removes rows of zones that are no longer secondaries.
///     </para>
/// </summary>
public class SecondaryZoneRefreshStateEntity : EntityBase
{
    /// <summary>
    ///     This property contains the identifier of the <see cref="ZoneEntity" /> this row
    ///     belongs to. It is unique: a zone has at most one row.
    /// </summary>
    public Guid ZoneId { get; set; }

    /// <summary>
    ///     This property contains the UTC time of the last successful refresh. It is read by zone
    ///     identifier only and never compared or ordered in a query, so it stays a plain
    ///     <see cref="DateTimeOffset" /> column.
    /// </summary>
    public DateTimeOffset LastRefreshedAtUtc { get; set; }
}
