using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Caching;

/// <summary>
///     This interface caches the results of iterative resolution in memory, keyed by (name,
///     type, class). It is both the answer cache and, since NS and address RRsets are cached like
///     any other type, the store of known name servers that lets
///     <see cref="Resolution.IterativeResolutionEngine" /> start below the root.
///     <see cref="RecursiveCache" /> implements it; cached TTLs are returned as stored.
/// </summary>
public interface IRecursiveCache
{
    /// <summary>
    ///     This method looks up a cached entry.
    /// </summary>
    /// <param name="name">The name to look up.</param>
    /// <param name="type">The record type to look up.</param>
    /// <param name="recordClass">The class to look up.</param>
    /// <returns>The cached entry, or <see langword="null" /> if nothing is cached, or the cached entry has expired.</returns>
    RecursiveCacheEntry TryGet(
        DnsName name,
        DnsResourceRecordTypeEnum type,
        DnsClassEnum recordClass
    );

    /// <summary>
    ///     This method looks up a cached entry even if it has expired, as long as it is within
    ///     <paramref name="maxStaleAge" /> of expiry. It is RFC 8767 serve-stale, which
    ///     <see cref="Resolution.IterativeResolutionEngine" /> uses only after a fresh attempt has
    ///     failed (see <see cref="RecursiveServerOptions.EnableServeStale" />). A hit counts as an
    ///     access for prefetch, like <see cref="TryGet" />.
    /// </summary>
    /// <param name="name">The name to look up.</param>
    /// <param name="type">The record type to look up.</param>
    /// <param name="recordClass">The class to look up.</param>
    /// <param name="maxStaleAge">How far past its expiry a cache entry may still be served.</param>
    /// <returns>
    ///     The cached entry, fresh or stale up to <paramref name="maxStaleAge" />, or
    ///     <see langword="null" /> if nothing usable is cached.
    /// </returns>
    RecursiveCacheEntry TryGetStale(
        DnsName name,
        DnsResourceRecordTypeEnum type,
        DnsClassEnum recordClass,
        TimeSpan maxStaleAge
    );

    /// <summary>
    ///     This method stores a positive answer, replacing whatever was previously cached for
    ///     the same (name, type, class). The entry lives for the lowest TTL among
    ///     <paramref name="records" />. An empty list is ignored.
    /// </summary>
    /// <param name="name">The answer's owner name.</param>
    /// <param name="type">The answer's record type.</param>
    /// <param name="recordClass">The answer's class.</param>
    /// <param name="records">The RRset to cache.</param>
    /// <param name="dnssecRecords">Whatever DNSSEC content covered <paramref name="records" />, if any.</param>
    void StorePositive(
        DnsName name,
        DnsResourceRecordTypeEnum type,
        DnsClassEnum recordClass,
        IReadOnlyList<DnsResourceRecord> records,
        IReadOnlyList<DnsResourceRecord> dnssecRecords
    );

    /// <summary>
    ///     This method stores a negative (NXDOMAIN/NODATA) answer for the lesser of the SOA
    ///     record's TTL and its MINIMUM field (RFC 2308).
    /// </summary>
    /// <param name="name">The name that does not exist, or lacks the queried type.</param>
    /// <param name="type">The record type that was queried for.</param>
    /// <param name="recordClass">The query's class.</param>
    /// <param name="responseCode">
    ///     <see cref="DnsResponseCodeEnum.NxDomain" /> or <see cref="DnsResponseCodeEnum.NoError" />
    ///     (NODATA).
    /// </param>
    /// <param name="soa">The zone's SOA record, which sets the negative-caching lifetime.</param>
    /// <param name="dnssecRecords">The SOA's RRSIG and the denial-of-existence proof, if any.</param>
    void StoreNegative(
        DnsName name,
        DnsResourceRecordTypeEnum type,
        DnsClassEnum recordClass,
        DnsResponseCodeEnum responseCode,
        DnsResourceRecord soa,
        IReadOnlyList<DnsResourceRecord> dnssecRecords
    );

    /// <summary>
    ///     This method removes expired entries that are past the serve-stale window, and enforces
    ///     <see cref="RecursiveServerOptions.MaxCacheEntries" /> by evicting the soonest-to-expire
    ///     entries, publishing <see cref="Ninlil.Dns.Server.Core.Events.DnsCacheEvictedEvent" />
    ///     for the evictions.
    /// </summary>
    /// <returns>How many entries were evicted.</returns>
    int EvictExpiredAndOverCapacity();

    /// <summary>
    ///     This method finds every unexpired entry that is within <paramref name="prefetchWindow" />
    ///     of expiry and was looked up within <paramref name="recentAccessWindow" />: the candidates
    ///     <see cref="Caching.RecursiveCachePrefetchService" /> refreshes. Only positive entries are
    ///     returned; refreshing a negative answer early saves little.
    /// </summary>
    /// <param name="prefetchWindow">How close to expiry an entry must be to qualify.</param>
    /// <param name="recentAccessWindow">How recently an entry must have been looked up to qualify.</param>
    /// <returns>Every qualifying entry's name, type and class.</returns>
    IReadOnlyList<(
        DnsName Name,
        DnsResourceRecordTypeEnum Type,
        DnsClassEnum Class
    )> GetPrefetchCandidates(TimeSpan prefetchWindow, TimeSpan recentAccessWindow);
}
