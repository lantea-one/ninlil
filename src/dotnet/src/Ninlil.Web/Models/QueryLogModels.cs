using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries a single persisted query log entry's own fields, for
///     <c>QueryLogController</c>'s own search response. No secret material is involved anywhere
///     in this entity, so, unlike several other entity types in this project, there is no
///     reveal-once policy to apply here at all -- every field is always fully visible to any
///     caller holding <c>querylog:read</c>.
/// </summary>
/// <param name="Id">This entry's own identifier.</param>
/// <param name="OccurredAtUtc">The UTC date and time the query finished being answered.</param>
/// <param name="ClientAddress">The querying client's own source address, or <see langword="null" /> if none was known.</param>
/// <param name="Protocol">The transport the query arrived over.</param>
/// <param name="QueriedName">The queried name's own presentation form, or <see langword="null" /> if the query carried no question at all.</param>
/// <param name="QueriedType">The queried record type, or <see langword="null" /> if the query carried no question at all.</param>
/// <param name="ResponseCode">The response code the answer was actually sent with.</param>
/// <param name="AnswerCount">How many records the answer's own Answer section carried.</param>
/// <param name="ClientIdentifier">
///     The querying client's own persistent identifier (a MAC address or DUID), resolved once at
///     write time, or <see langword="null" /> when it was never resolved or the configured query
///     logging privacy level suppresses it -- see
///     <see cref="Ninlil.Dns.Server.QueryLog.DnsQueryLogSubscriber" />'s own doc comment for the
///     full reasoning.
/// </param>
/// <param name="ClientProtocolVersion">
///     Which of the two disjoint identifier spaces <paramref name="ClientIdentifier" /> belongs
///     to, or <see langword="null" /> whenever <paramref name="ClientIdentifier" /> itself is
///     <see langword="null" />.
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
///     This class carries the outcome of an explicit "clear the log" action.
/// </summary>
/// <param name="DeletedCount">How many entries were permanently removed.</param>
public record QueryLogClearResult(int DeletedCount);
