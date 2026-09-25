using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Caching;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Caching;

/// <summary>
///     This record holds one <see cref="IRecursiveCache" /> entry: a positive RRset or a negative
///     answer, with any DNSSEC records that came with it.
/// </summary>
/// <param name="IsNegative">
///     Whether this entry is a negative (NXDOMAIN/NODATA) answer rather than an RRset.
/// </param>
/// <param name="Records">
///     The cached RRset, or an empty list for a negative entry.
/// </param>
/// <param name="DnssecRecords">
///     The RRSIG, NSEC, NSEC3 or DNSKEY records that came with <see cref="Records" /> (for a
///     negative entry, the SOA's signatures and the denial proof), if DNSSEC data was requested.
/// </param>
/// <param name="NegativeResponseCode">
///     For a negative entry, whether the name did not exist
///     (<see cref="DnsResponseCodeEnum.NxDomain" />) or lacked the queried type
///     (<see cref="DnsResponseCodeEnum.NoError" />, NODATA). Meaningless for a positive entry.
/// </param>
/// <param name="Soa">
///     For a negative entry, the zone's SOA record, needed to rebuild an RFC 2308 negative
///     response from cache. <see langword="null" /> for a
///     positive entry.
/// </param>
/// <param name="ExpiresAtUtc">When this entry stops being fresh.</param>
public sealed record RecursiveCacheEntry(
    bool IsNegative,
    IReadOnlyList<DnsResourceRecord> Records,
    IReadOnlyList<DnsResourceRecord> DnssecRecords,
    DnsResponseCodeEnum NegativeResponseCode,
    DnsResourceRecord Soa,
    DateTimeOffset ExpiresAtUtc
) : IDnsCacheEntry
{
    /// <summary>
    ///     This property indicates whether this entry has expired, by the system clock (not the
    ///     cache store's clock).
    /// </summary>
    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAtUtc;
}
