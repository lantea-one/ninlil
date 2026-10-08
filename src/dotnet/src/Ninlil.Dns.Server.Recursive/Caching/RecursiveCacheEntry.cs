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
/// <param name="StoredAtUtc">When this entry was stored, which its records' TTLs count down from.</param>
public sealed record RecursiveCacheEntry(
    bool IsNegative,
    IReadOnlyList<DnsResourceRecord> Records,
    IReadOnlyList<DnsResourceRecord> DnssecRecords,
    DnsResponseCodeEnum NegativeResponseCode,
    DnsResourceRecord Soa,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset StoredAtUtc
) : IDnsCacheEntry
{
    /// <summary>
    ///     This property indicates whether this entry has expired, by the system clock (not the
    ///     cache store's clock).
    /// </summary>
    public bool IsExpired => DateTimeOffset.UtcNow >= ExpiresAtUtc;

    /// <summary>
    ///     This method returns a copy of this entry with every record's TTL reduced by how long
    ///     the entry has been cached, so no client or downstream cache keeps the data longer than
    ///     its owner allowed. A TTL never goes below zero.
    /// </summary>
    /// <param name="now">The current time on the cache's clock.</param>
    /// <returns>The aged copy, or this entry itself when no whole second has passed.</returns>
    public RecursiveCacheEntry AgedTo(DateTimeOffset now)
    {
        uint elapsedSeconds = (uint)Math.Clamp((now - StoredAtUtc).TotalSeconds, 0, uint.MaxValue);

        if (elapsedSeconds == 0)
            return this;

        DnsResourceRecord Age(DnsResourceRecord record) =>
            new(
                record.Name,
                record.Type,
                record.Class,
                record.TimeToLive > elapsedSeconds ? record.TimeToLive - elapsedSeconds : 0,
                record.Data
            );

        return this with
        {
            Records = [.. Records.Select(Age)],
            DnssecRecords = [.. DnssecRecords.Select(Age)],
            Soa = Soa is null ? null : Age(Soa),
        };
    }

    /// <summary>
    ///     This method returns a copy of this entry with every record's TTL (records, DNSSEC
    ///     records and the SOA) set to one value. Serve-stale answers use it so a stale answer
    ///     is not cached downstream for its original lifetime (RFC 8767).
    /// </summary>
    /// <param name="timeToLive">The TTL in seconds to set.</param>
    /// <returns>The copy.</returns>
    public RecursiveCacheEntry WithTtl(uint timeToLive)
    {
        DnsResourceRecord Set(DnsResourceRecord record) =>
            new(record.Name, record.Type, record.Class, timeToLive, record.Data);

        return this with
        {
            Records = [.. Records.Select(Set)],
            DnssecRecords = [.. DnssecRecords.Select(Set)],
            Soa = Soa is null ? null : Set(Soa),
        };
    }
}
