using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries one query log entry, as returned in the pages of
///     <c>GET /api/query-log</c>. Nothing in it is secret, so every field is visible to any caller
///     holding <c>querylog:read</c>. What an entry contains depends on the privacy level in force
///     when it was written.
/// </summary>
/// <param name="Id">The entry's identifier.</param>
/// <param name="OccurredAtUtc">The UTC date and time the query was answered.</param>
/// <param name="ClientAddress">The client's source address, or <see langword="null" /> if it was not known or not kept.</param>
/// <param name="Protocol">The transport the query arrived over.</param>
/// <param name="QueriedName">The queried name in presentation form, or <see langword="null" /> if the query had no question.</param>
/// <param name="QueriedType">The queried record type, or <see langword="null" /> if the query had no question.</param>
/// <param name="ResponseCode">The response code the answer was sent with.</param>
/// <param name="AnswerCount">How many records the answer section held.</param>
/// <param name="ClientIdentifier">
///     The client's persistent identifier (a MAC address or DUID), resolved when the entry was
///     written, or <see langword="null" /> when it was not resolved or the privacy level hides it
///     (see <see cref="Ninlil.Dns.Server.QueryLog.DnsQueryLogSubscriber" />).
/// </param>
/// <param name="ClientProtocolVersion">
///     Which identifier space <paramref name="ClientIdentifier" /> belongs to, or
///     <see langword="null" /> when <paramref name="ClientIdentifier" /> is <see langword="null" />.
/// </param>
public record DnsQueryLogEntryDetail(
    Guid Id,
    DateTimeOffset OccurredAtUtc,
    string ClientAddress,
    DnsTransportProtocolEnum Protocol,
    string QueriedName,
    DnsResourceRecordTypeEnum? QueriedType,
    DnsResponseCodeEnum ResponseCode,
    int AnswerCount,
    string ClientIdentifier,
    InternetProtocolVersionEnum? ClientProtocolVersion
);

/// <summary>
///     This class carries the outcome of clearing the query log (<c>DELETE /api/query-log</c>).
/// </summary>
/// <param name="DeletedCount">How many entries were permanently removed.</param>
public record QueryLogClearResult(int DeletedCount);
