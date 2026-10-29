// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents one immutable step in a zone's history: the records added and
///     removed to move the zone from <see cref="OldSerial" /> to <see cref="NewSerial" />. IXFR
///     (RFC 1995) is defined in these terms, as a sequence of {old SOA, deleted records, new SOA,
///     added records} steps, so a chain of entries in serial order lets the IXFR responder send
///     a delta instead of a full transfer.
///     <para>
///         The journal is append-only: an entry is never updated. It derives from
///         <see cref="EntityBase" /> rather than <see cref="AuditableEntityBase" />, like
///         <see cref="LogEntity" /> and <see cref="MetricEntity" />.
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
    ///     This property contains the zone's SOA serial number before this change. It is a wider
    ///     signed type than the 32-bit unsigned wire field because not every relational provider
    ///     supports unsigned integers.
    /// </summary>
    public long OldSerial { get; set; }

    /// <summary>
    ///     This property contains the zone's SOA serial number after this change.
    /// </summary>
    public long NewSerial { get; set; }

    /// <summary>
    ///     This property contains every record removed by this change, as a JSON array of
    ///     <see cref="RecordEntity" /> snapshots. Snapshots are needed because the live rows are gone
    ///     once the change is applied.
    /// </summary>
    public string RemovedRecordsJson { get; set; }

    /// <summary>
    ///     This property contains every record added by this change, serialized the same way as
    ///     <see cref="RemovedRecordsJson" />.
    /// </summary>
    public string AddedRecordsJson { get; set; }

    /// <summary>
    ///     This property contains the zone's SOA record as it was before this change, as one JSON
    ///     <see cref="RecordEntity" /> snapshot (owner name, class, TTL and data) in the same
    ///     format as <see cref="RemovedRecordsJson" />. It is the SOA RR that RFC 1995 §4 puts at
    ///     the start of the step's deleted records. It is null on entries written before SOA
    ///     history was recorded, and on the first entry of a zone, which has no older version.
    /// </summary>
    public string OldSoaRecordJson { get; set; }

    /// <summary>
    ///     This property contains the zone's SOA record as it was after this change, serialized
    ///     the same way as <see cref="OldSoaRecordJson" />. It is the SOA RR that RFC 1995 §4 puts
    ///     at the start of the step's added records. It is null on entries written before SOA
    ///     history was recorded.
    /// </summary>
    public string NewSoaRecordJson { get; set; }
}
