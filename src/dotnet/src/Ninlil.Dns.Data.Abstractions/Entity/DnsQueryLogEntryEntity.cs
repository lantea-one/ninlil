using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents one answered DNS query in the persistent query log, which an
///     administrator can search by client, domain, record type, response code and time range.
///     <c>Server.QueryLog.DnsQueryLogSubscriber</c> writes it from the published query event, so
///     its fields are what that event carries, subject to the <see cref="QueryLogPrivacyLevelEnum" />.
///     <para>
///         Entries are append-only (only retention removes them), so this derives from
///         <see cref="EntityBase" /> rather than <see cref="AuditableEntityBase" />.
///     </para>
/// </summary>
public class DnsQueryLogEntryEntity : EntityBase
{
    /// <summary>
    ///     This property contains the UTC date and time the query was answered. Search, filters and
    ///     retention use it rather than <see cref="EntityBase.CreatedAtUtc" /> (when the row was
    ///     written), which can lag slightly because entries are written in the background.
    /// </summary>
    public DateTimeOffset OccurredAtUtc { get; set; }

    /// <summary>
    ///     This property contains the client's source address as text, or <see langword="null" />
    ///     if the event carried none or the privacy level removed it.
    /// </summary>
    public string ClientAddress { get; set; }

    /// <summary>
    ///     This property contains the transport the query arrived over.
    /// </summary>
    public DnsTransportProtocolEnum Protocol { get; set; }

    /// <summary>
    ///     This property contains the queried name in presentation form, or <see langword="null" />
    ///     if the query had no question or the privacy level removed it.
    /// </summary>
    public string QueriedName { get; set; }

    /// <summary>
    ///     This property contains the queried record type, or <see langword="null" /> if the query
    ///     had no question.
    /// </summary>
    public DnsResourceRecordTypeEnum? QueriedType { get; set; }

    /// <summary>
    ///     This property contains the response code the answer was sent with.
    /// </summary>
    public DnsResponseCodeEnum ResponseCode { get; set; }

    /// <summary>
    ///     This property contains how many records the Answer section carried.
    /// </summary>
    public int AnswerCount { get; set; }

    /// <summary>
    ///     This property contains the client's persistent identifier (a colon-separated MAC address
    ///     for an IPv4 client, or a DUID for an IPv6 one), resolved through
    ///     <c>Ninlil.Dns.Server.Core.ClientIdentity.IClientIdentityResolver</c> when the entry was
    ///     written. It is never worked out later from <see cref="ClientAddress" />, because an address
    ///     can move to another device (a DHCP reassignment, for example), which would misattribute
    ///     history. It is <see langword="null" /> when no resolver was available, the resolver had no
    ///     mapping, or the privacy level was <see cref="QueryLogPrivacyLevelEnum.ClientAnonymized" /> or
    ///     higher. It follows the same privacy levels as <see cref="ClientAddress" />; see
    ///     <c>Server.QueryLog.DnsQueryLogSubscriber</c>.
    /// </summary>
    public string ClientIdentifier { get; set; }

    /// <summary>
    ///     This property indicates which of the two separate identifier spaces
    ///     <see cref="ClientIdentifier" /> belongs to. The two fields are always set or
    ///     <see langword="null" /> together.
    /// </summary>
    public InternetProtocolVersionEnum? ClientProtocolVersion { get; set; }
}
