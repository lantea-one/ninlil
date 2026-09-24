// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents a single, immutable step in a zone's history: the exact records
///     added and removed to move the zone from <see cref="OldSerial" /> to <see cref="NewSerial" />.
///     RFC 1995's incremental zone transfer (IXFR) is defined in exactly these terms -- a
///     sequence of {old SOA, deleted records, new SOA, added records} steps -- so a chain of
///     these entries, in serial order, is what lets the IXFR responder serve a real,
///     precise delta instead of falling back to a full zone transfer for every request.
///     <para>
///         This is append-only: once written, an entry is never updated or deleted (it derives
///         from <see cref="EntityBase" /> rather than <see cref="AuditableEntityBase" />, the
///         same way <see cref="LogEntity" /> and <see cref="MetricEntity" /> are), the same way
///         the zone history it records can't be edited after the fact either.
///     </para>
/// </summary>
public class ZoneChangeJournalEntryEntity : EntityBase
{
    /// <summary>
    ///     This property contains the identifier of the <see cref="ZoneEntity" /> this entry
    ///     belongs to.
    /// </summary>
    public Guid ZoneId { get; set; }

    /// <summary>
    ///     This property contains the zone's SOA serial number before this change, stored as a
    ///     wider signed type than the wire protocol's own 32-bit unsigned field to avoid
    ///     unsigned-integer support gaps across the four relational providers this entity is
    ///     stored on.
    /// </summary>
    public long OldSerial { get; set; }

    /// <summary>
    ///     This property contains the zone's SOA serial number after this change.
    /// </summary>
    public long NewSerial { get; set; }

    /// <summary>
    ///     This property contains every record removed by this change, serialized as a JSON
    ///     array of <see cref="RecordEntity" /> snapshots -- not a reference to the live
    ///     <see cref="RecordEntity" /> rows, which are already gone by the time this change has
    ///     been applied.
    /// </summary>
    public string RemovedRecordsJson { get; set; }

    /// <summary>
    ///     This property contains every record added by this change, serialized the same way as
    ///     <see cref="RemovedRecordsJson" />.
    /// </summary>
    public string AddedRecordsJson { get; set; }
}
