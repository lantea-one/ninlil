using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents a single DNS resource record stored inside a <see cref="ZoneEntity" />.
///     The record's type-specific data (an IPv4 address for an <see cref="DnsResourceRecordTypeEnum.A" />
///     record, a target and weight for an <see cref="DnsResourceRecordTypeEnum.Srv" />
///     record, and so on) is intentionally stored here as an opaque JSON payload rather than as a
///     dozen mutually-exclusive typed columns: the strongly-typed model for each record type
///     belongs to the wire-format layer built in a later phase, and this data layer's only job is
///     to keep whatever that layer hands it exactly as given.
/// </summary>
public class RecordEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains the identifier of the <see cref="ZoneEntity" /> this record
    ///     belongs to.
    /// </summary>
    public Guid ZoneId { get; set; }

    /// <summary>
    ///     This property contains the record's owner name. For a record at the zone's apex, this
    ///     is the same as the zone's own name.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     This property contains the record's DNS TYPE.
    /// </summary>
    public DnsResourceRecordTypeEnum Type { get; set; }

    /// <summary>
    ///     This property contains the record's DNS CLASS. Every record Ninlil serves uses
    ///     <see cref="DnsClassEnum.Internet" />; the field exists for completeness and forward
    ///     compatibility rather than because a different value is expected in practice.
    /// </summary>
    public DnsClassEnum Class { get; set; } = DnsClassEnum.Internet;

    /// <summary>
    ///     This property contains the record's time-to-live, in seconds -- how long a resolver
    ///     that caches this record is allowed to keep using its cached copy before asking again.
    /// </summary>
    public uint TimeToLiveSeconds { get; set; }

    /// <summary>
    ///     This property contains the record's type-specific data, serialized as a JSON object
    ///     whose shape depends on <see cref="Type" />.
    /// </summary>
    public string DataJson { get; set; }

    /// <summary>
    ///     This property indicates whether the record is currently disabled. A disabled record is
    ///     kept in the zone but omitted from answers.
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    ///     This property contains free-form operator notes about the record.
    /// </summary>
    public string Comments { get; set; }

    /// <summary>
    ///     This property marks a record as local-only: it is never included in an outgoing
    ///     AXFR/IXFR transfer or cloud-provider export push, and never overwritten or deleted by
    ///     an incoming AXFR/IXFR apply or cloud-provider import pull, regardless of what a
    ///     zone's remote master/provider does or does not have. This is what lets an otherwise
    ///     synced <see cref="ZoneEntity" /> (<see cref="ZoneTypeEnum.Secondary" />,
    ///     <see cref="ZoneTypeEnum.CloudPrimary" />, or <see cref="ZoneTypeEnum.CloudSecondary" />)
    ///     still carry records that exist only on this server -- a deliberately simpler
    ///     alternative to full per-client split-horizon views.
    /// </summary>
    public bool ExcludeFromSync { get; set; }

    /// <summary>
    ///     This property identifies which of Ninlil's own automated record-management
    ///     features created this record, or <see langword="null" /> for a record an operator
    ///     added by hand (via REST, RFC 2136 dynamic update, AXFR/IXFR, or cloud sync). A feature
    ///     that reconciles a zone's own record set against some external source of truth (CAA
    ///     auto-management, PTR/reverse-zone sync) must only ever add, update, or remove a record
    ///     it created itself -- checking this field is how it tells its own record apart from an
    ///     operator-managed one it must never touch.
    /// </summary>
    public RecordManagedByFeatureEnum? ManagedByFeature { get; set; }
}
