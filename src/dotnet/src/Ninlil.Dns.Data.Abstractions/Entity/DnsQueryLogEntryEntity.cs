using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents a single, already-answered DNS query captured for the persistent
///     query log -- the searchable historical record an administrator can filter by client
///     address, domain, record type, response code, and time range, without having to watch a
///     live, session-only feed to catch it. Every field here is copied from an already-published
///     query-lifecycle event rather than computed independently, so this entity's own shape
///     tracks whatever that event already carries cheaply.
///     <para>
///         A purely append-only record -- nothing ever edits an existing entry once written, only
///         a retention sweep ever removes one -- so this derives directly from
///         <see cref="EntityBase" /> rather than <see cref="AuditableEntityBase" />, the same
///         "log entry" example <see cref="AuditableEntityBase" />'s own doc comment already names
///         as not needing an <c>UpdatedAtUtc</c> column.
///     </para>
/// </summary>
public class DnsQueryLogEntryEntity : EntityBase
{
    /// <summary>
    ///     This property contains the UTC date and time the query finished being answered. This
    ///     is the moment the search/filter/retention logic reasons about, not
    ///     <see cref="EntityBase.CreatedAtUtc" /> (when the row itself was written to storage),
    ///     which can lag slightly behind it since persistence happens on a detached background
    ///     task rather than synchronously during query answering.
    /// </summary>
    public DateTimeOffset OccurredAtUtc { get; set; }

    /// <summary>
    ///     This property contains the querying client's own source address, in its textual
    ///     presentation form, or <see langword="null" /> if the answering event carried none.
    /// </summary>
    public string ClientAddress { get; set; }

    /// <summary>
    ///     This property contains the transport the query arrived over.
    /// </summary>
    public DnsTransportProtocolEnum Protocol { get; set; }

    /// <summary>
    ///     This property contains the queried name's own presentation form, or
    ///     <see langword="null" /> if the query carried no question at all.
    /// </summary>
    public string QueriedName { get; set; }

    /// <summary>
    ///     This property contains the queried record type, or <see langword="null" /> if the
    ///     query carried no question at all.
    /// </summary>
    public DnsResourceRecordTypeEnum? QueriedType { get; set; }

    /// <summary>
    ///     This property contains the response code the answer was actually sent with.
    /// </summary>
    public DnsResponseCodeEnum ResponseCode { get; set; }

    /// <summary>
    ///     This property contains how many records the answer's own Answer section carried.
    /// </summary>
    public int AnswerCount { get; set; }

    /// <summary>
    ///     This property contains the querying client's own persistent identifier (a colon-separated
    ///     hardware/MAC address for an IPv4 client, or a DHCP Unique Identifier (DUID) for an IPv6
    ///     one), resolved once, at the moment this entry was written, via
    ///     <c>Ninlil.Dns.Server.Core.ClientIdentity.IClientIdentityResolver</c> -- never re-derived
    ///     later from <see cref="ClientAddress" />, since an address can be reassigned to a
    ///     completely different physical device long after this entry was logged (a DHCP lease
    ///     reassignment, a device rejoining the network with a new address), which would silently
    ///     misattribute history if resolution were instead deferred to read time.
    ///     <see langword="null" /> when no identity-resolution infrastructure was available, the
    ///     resolver had no mapping for the querying address, or <see cref="QueryLogPrivacyLevelEnum" />
    ///     was already at or above <see cref="QueryLogPrivacyLevelEnum.ClientAnonymized" /> at write
    ///     time -- see <c>Server.QueryLog.DnsQueryLogSubscriber</c>'s own doc comment for why this
    ///     field is suppressed at exactly the same privacy levels that already anonymize/suppress
    ///     <see cref="ClientAddress" />, never a separate, independently-drifting privacy decision.
    /// </summary>
    public string ClientIdentifier { get; set; }

    /// <summary>
    ///     This property indicates which of the two disjoint identifier spaces
    ///     <see cref="ClientIdentifier" /> belongs to, or <see langword="null" /> whenever
    ///     <see cref="ClientIdentifier" /> itself is <see langword="null" /> -- the two fields are
    ///     always set, or left null, together.
    /// </summary>
    public InternetProtocolVersionEnum? ClientProtocolVersion { get; set; }
}
