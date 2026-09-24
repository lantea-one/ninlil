using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This class carries every optional filter <see cref="IDnsQueryLogRepository.SearchAsync" />
///     and <see cref="IDnsQueryLogRepository.CountAsync" /> accept, bundled into one value rather
///     than a long, repeated parameter list across the repository interface, both of its storage
///     implementations, and the REST controller that builds one from query-string parameters. A
///     <see langword="null" /> field means "do not filter on this axis at all" -- an entirely
///     empty instance matches every entry in the log.
/// </summary>
/// <param name="ClientAddress">
///     An exact match against <see cref="Entity.DnsQueryLogEntryEntity.ClientAddress" />'s own
///     textual form, or <see langword="null" /> to match any client.
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
///     An exact match against <see cref="Entity.DnsQueryLogEntryEntity.ClientIdentifier" /> -- the
///     resolved, persistent client identifier a per-client activity view narrows by, independent
///     of whatever source address a given entry happened to carry -- or <see langword="null" /> to
///     match any (or no) resolved client.
/// </param>
/// <param name="ClientProtocolVersion">
///     An exact match against <see cref="Entity.DnsQueryLogEntryEntity.ClientProtocolVersion" />,
///     narrowing which of the two disjoint identifier spaces <see cref="ClientIdentifier" /> must
///     belong to -- combined with <see cref="ClientIdentifier" /> when both are given, mirroring
///     <c>DnsClientEntity</c>'s own composite uniqueness pair, or <see langword="null" /> to match
///     either space.
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
