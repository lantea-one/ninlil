using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This record carries the optional filters that <see cref="IDnsQueryLogRepository.SearchAsync" />
///     and <see cref="IDnsQueryLogRepository.CountAsync" /> accept, as one value instead of a long
///     parameter list repeated in the interface, both storage implementations and the REST
///     controller that builds it from query-string parameters. A <see langword="null" /> field is
///     not applied, so an empty instance matches every entry.
/// </summary>
/// <param name="ClientAddress">
///     An exact match against the text of <see cref="Entity.DnsQueryLogEntryEntity.ClientAddress" />,
///     or <see langword="null" /> to match any client.
/// </param>
/// <param name="DomainContains">
///     A case-insensitive substring match against
///     <see cref="Entity.DnsQueryLogEntryEntity.QueriedName" />, or <see langword="null" /> to
///     match any (or no) queried name.
/// </param>
/// <param name="QueriedType">
///     An exact match against <see cref="Entity.DnsQueryLogEntryEntity.QueriedType" />, or
///     <see langword="null" /> to match any record type.
/// </param>
/// <param name="ResponseCode">
///     An exact match against <see cref="Entity.DnsQueryLogEntryEntity.ResponseCode" />, or
///     <see langword="null" /> to match any response code.
/// </param>
/// <param name="FromUtc">
///     The inclusive start of the time range to search, or <see langword="null" /> for no lower
///     bound.
/// </param>
/// <param name="ToUtc">
///     The inclusive end of the time range to search, or <see langword="null" /> for no upper
///     bound.
/// </param>
/// <param name="ClientIdentifier">
///     An exact match against <see cref="Entity.DnsQueryLogEntryEntity.ClientIdentifier" />, the
///     resolved client identifier a per-client activity view filters by whatever the entry's
///     source address, or <see langword="null" /> to match any (or no) resolved client.
/// </param>
/// <param name="ClientProtocolVersion">
///     An exact match against <see cref="Entity.DnsQueryLogEntryEntity.ClientProtocolVersion" />,
///     choosing which of the two separate identifier spaces <see cref="ClientIdentifier" />
///     belongs to (the same pair that makes a <c>DnsClientEntity</c> unique), or
///     <see langword="null" /> to match either.
/// </param>
public sealed record DnsQueryLogSearchCriteria(
    string ClientAddress = null,
    string DomainContains = null,
    DnsResourceRecordTypeEnum? QueriedType = null,
    DnsResponseCodeEnum? ResponseCode = null,
    DateTimeOffset? FromUtc = null,
    DateTimeOffset? ToUtc = null,
    string ClientIdentifier = null,
    InternetProtocolVersionEnum? ClientProtocolVersion = null
);
