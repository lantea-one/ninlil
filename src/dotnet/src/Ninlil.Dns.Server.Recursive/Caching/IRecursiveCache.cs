using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Caching;

/// <summary>
///     This interface caches the results of iterative resolution, in memory only, since a
///     resolver cache is inherently ephemeral and TTL-bound and so never belongs in the database
///     the way zone data does. One cache, keyed by (name, type, class),
///     serves both an ordinary answer cache and -- since an NS RRset is cached exactly the same
///     way as any other type -- the "which name servers do I already know about for this name's
///     ancestors" lookup <see cref="Resolution.IterativeResolutionEngine" /> needs to avoid
///     re-querying the root for every single query.
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
    ///     This method looks up a cached entry regardless of whether it has already expired, as
    ///     long as it is still within <paramref name="maxStaleAge" /> of its own expiry -- RFC
    ///     8767 "serve stale" support, consulted by <see cref="Resolution.IterativeResolutionEngine" />
    ///     only once a genuine attempt at a fresh answer has already failed (see
    ///     <see cref="RecursiveServerOptions.EnableServeStale" />'s own doc comment). Updates the
    ///     same last-accessed bookkeeping <see cref="TryGet" /> does on a hit, since serving an
    ///     entry stale is still real, current evidence this name is actively being queried.
    /// </summary>
    /// <param name="name">The name to look up.</param>
    /// <param name="type">The record type to look up.</param>
    /// <param name="recordClass">The class to look up.</param>
    /// <param name="maxStaleAge">How far past its own expiry a cache entry may still be served from.</param>
    /// <returns>
    ///     The cached entry, however stale (up to <paramref name="maxStaleAge" />), or
    ///     <see langword="null" /> if nothing is cached at all, or what is cached has aged past
    ///     even that grace window.
    /// </returns>
    RecursiveCacheEntry TryGetStale(
        DnsName name,
        DnsResourceRecordTypeEnum type,
        DnsClassEnum recordClass,
        TimeSpan maxStaleAge
    );

    /// <summary>
    ///     This method stores a positive answer, replacing whatever was previously cached for
    ///     the same (name, type, class). The cached TTL is the lowest TTL among
    ///     <paramref name="records" />, the conventional, conservative choice when an RRset's
    ///     individual records could, in principle, carry slightly different TTLs.
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
    ///     This method stores a negative (NXDOMAIN/NODATA) answer, per RFC 2308, capped at the
    ///     authoritative zone's own SOA-minimum TTL.
    /// </summary>
    /// <param name="name">The name that does not exist, or lacks the queried type.</param>
    /// <param name="type">The record type that was queried for.</param>
    /// <param name="recordClass">The query's class.</param>
    /// <param name="responseCode">
    ///     <see cref="DnsResponseCodeEnum.NxDomain" /> or <see cref="DnsResponseCodeEnum.NoError" />
    ///     (NODATA).
    /// </param>
    /// <param name="soa">The authoritative zone's own SOA record, for its negative-caching minimum.</param>
    /// <param name="dnssecRecords">The SOA's own RRSIG and denial-of-existence proof, if any.</param>
    void StoreNegative(
        DnsName name,
        DnsResourceRecordTypeEnum type,
        DnsClassEnum recordClass,
        DnsResponseCodeEnum responseCode,
        DnsResourceRecord soa,
        IReadOnlyList<DnsResourceRecord> dnssecRecords
    );

    /// <summary>
    ///     This method removes every expired entry, publishing
    ///     <see cref="Ninlil.Dns.Server.Core.Events.DnsCacheEvictedEvent" /> for each one --
    ///     this cache's own producer of that event. Also enforces
    ///     <see cref="RecursiveServerOptions.MaxCacheEntries" /> by evicting the
    ///     soonest-to-expire entries first when the cache has grown past it, even among entries
    ///     that have not expired yet.
    /// </summary>
    /// <returns>How many entries were evicted.</returns>
    int EvictExpiredAndOverCapacity();

    /// <summary>
    ///     This method finds every cache entry that is both nearing its own TTL expiry and has
    ///     genuinely been queried recently -- the exact candidate set
    ///     <see cref="Caching.RecursiveCachePrefetchService" /> proactively refreshes. Only
    ///     positive (non-negative) entries are ever returned: a negative (NXDOMAIN/NODATA) answer
    ///     nearing expiry is simply left to the next real client query, since re-confirming that a
    ///     name still does not exist has none of positive prefetch's own latency-hiding value.
    /// </summary>
    /// <param name="prefetchWindow">How close to expiry an entry must be to qualify.</param>
    /// <param name="recentAccessWindow">How recently an entry must have actually been looked up to qualify.</param>
    /// <returns>Every qualifying entry's own name, type, and class.</returns>
    IReadOnlyList<(
        DnsName Name,
        DnsResourceRecordTypeEnum Type,
        DnsClassEnum Class
    )> GetPrefetchCandidates(TimeSpan prefetchWindow, TimeSpan recentAccessWindow);
}
