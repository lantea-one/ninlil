using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Caching;

/// <summary>
///     This class holds one cached answer -- positive or negative -- alongside whatever
///     DNSSEC content covered it, if any was requested and received.
/// </summary>
/// <param name="IsNegative">
///     Whether this entry represents a negative (NXDOMAIN/NODATA) answer rather than a real
///     RRset.
/// </param>
/// <param name="Records">
///     The cached RRset, or an empty list for a negative entry.
/// </param>
/// <param name="DnssecRecords">
///     Whatever RRSIG/NSEC/NSEC3/DNSKEY records accompanied <see cref="Records" /> (or, for a
///     negative entry, the SOA and its own denial-of-existence proof), if DNSSEC data was
///     requested and received at all.
/// </param>
/// <param name="NegativeResponseCode">
///     For a negative entry, whether the name did not exist at all
///     (<see cref="DnsResponseCodeEnum.NxDomain" />) or simply lacked the queried type
///     (<see cref="DnsResponseCodeEnum.NoError" />, NODATA). Meaningless for a positive entry.
/// </param>
/// <param name="Soa">
///     For a negative entry, the authoritative zone's own SOA record, needed to rebuild an
///     RFC 2308-compliant negative response straight from cache. <see langword="null" /> for a
///     positive entry.
/// </param>
/// <param name="ExpiresAtUtc">The point this entry stops being valid to serve from cache at all.</param>
public sealed record RecursiveCacheEntry(
    bool IsNegative,
    IReadOnlyList<DnsResourceRecord> Records,
    IReadOnlyList<DnsResourceRecord> DnssecRecords,
    DnsResponseCodeEnum NegativeResponseCode,
    DnsResourceRecord Soa,
    DateTimeOffset ExpiresAtUtc
)
{
    /// <summary>
    ///     This property indicates whether this entry is still valid to serve from cache.
    /// </summary>
    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAtUtc;
}
