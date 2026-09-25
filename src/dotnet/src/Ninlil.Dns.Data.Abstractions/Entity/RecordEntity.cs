using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents one DNS resource record stored in a <see cref="ZoneEntity" />. The
///     type-specific data (an IPv4 address for an <see cref="DnsResourceRecordTypeEnum.A" /> record,
///     a target and weight for an <see cref="DnsResourceRecordTypeEnum.Srv" /> record, and so on)
///     is stored as a JSON payload rather than many mutually exclusive columns. The typed models
///     live in <c>Ninlil.Dns.Common.Wire.Records</c>; this layer stores the JSON as given.
/// </summary>
public class RecordEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains the identifier of the <see cref="ZoneEntity" /> this record
    ///     belongs to.
    /// </summary>
    public Guid ZoneId { get; set; }

    /// <summary>
    ///     This property contains the record's owner name. For a record at the zone apex it equals
    ///     the zone name.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     This property contains the record's DNS TYPE.
    /// </summary>
    public DnsResourceRecordTypeEnum Type { get; set; }

    /// <summary>
    ///     This property contains the record's DNS CLASS. Every record Ninlil serves uses
    ///     <see cref="DnsClassEnum.Internet" />; the field exists for completeness.
    /// </summary>
    public DnsClassEnum Class { get; set; } = DnsClassEnum.Internet;

    /// <summary>
    ///     This property contains the record's time-to-live, in seconds: how long a resolver may
    ///     cache it before asking again.
    /// </summary>
    public uint TimeToLiveSeconds { get; set; }

    /// <summary>
    ///     This property contains the record's type-specific data, as a JSON object whose shape
    ///     depends on <see cref="Type" />.
    /// </summary>
    public string DataJson { get; set; }

    /// <summary>
    ///     This property indicates whether the record is disabled. A disabled record stays in the
    ///     zone but is left out of answers.
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    ///     This property contains free-form operator notes about the record.
    /// </summary>
    public string Comments { get; set; }

    /// <summary>
    ///     This property marks a record as local-only. It is never sent in an outgoing AXFR/IXFR or
    ///     cloud-provider push, and never overwritten or deleted by an incoming AXFR/IXFR or
    ///     cloud-provider pull, whatever the remote side has. This lets a synced
    ///     <see cref="ZoneEntity" /> (<see cref="ZoneTypeEnum.Secondary" />,
    ///     <see cref="ZoneTypeEnum.CloudPrimary" /> or <see cref="ZoneTypeEnum.CloudSecondary" />) hold
    ///     records that exist only on this server, a simpler alternative to per-client split-horizon
    ///     views.
    /// </summary>
    public bool ExcludeFromSync { get; set; }

    /// <summary>
    ///     This property identifies which automated record-management feature created this record,
    ///     or <see langword="null" /> for a record that came from anywhere else (REST, RFC 2136
    ///     dynamic update, AXFR/IXFR or cloud sync). A reconciling feature (CAA auto-management,
    ///     reverse-zone sync, and the others in <see cref="RecordManagedByFeatureEnum" />) only adds,
    ///     updates or removes records it created, and uses this field to tell them apart.
    /// </summary>
    public RecordManagedByFeatureEnum? ManagedByFeature { get; set; }
}
