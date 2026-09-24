using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Core.Caching;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Caching;

/// <summary>
///     This class implements <see cref="IRecursiveCache" /> on top of the shared
///     <see cref="DnsCacheStore{TKey, TEntry}" />, keyed by (name, type, class). It is also the
///     recursive half of the operator-facing <see cref="IDnsCache" /> surface: its statistics,
///     flush and per-name removal are what the cache REST endpoint reports and acts on, and
///     <see cref="DnsCacheEvictionService" /> sweeps it alongside the forwarded-response cache.
///     <para>
///         Its hit and miss counts include the iterative engine's own internal lookups (name
///         server addresses, delegations, CNAME hops), not only lookups for a client's question,
///         so they describe how much upstream traffic the cache saves rather than a per-query
///         hit rate.
///     </para>
/// </summary>
/// <param name="options">
///     This cache's own capacity limit and serve-stale window, read live via
///     <see cref="IOptionsMonitor{TOptions}" /> -- see <see cref="Resolution.RecursiveQueryResolver" />'s
///     own doc comment for why this cache, registered as a singleton, cannot use the usual
///     snapshotted <see cref="IOptions{TOptions}" /> instead.
/// </param>
/// <param name="eventPublisher">The publisher this cache reports its own evictions through.</param>
public sealed class RecursiveCache(
    IOptionsMonitor<RecursiveServerOptions> options,
    IDnsServerEventPublisher eventPublisher
) : IRecursiveCache, IDnsCache
{
    /// <summary>
    ///     This constant contains this cache's <see cref="IDnsCache.Name" />.
    /// </summary>
    public const string CacheName = "recursive";

    private readonly DnsCacheStore<
        (DnsName Name, DnsResourceRecordTypeEnum Type, DnsClassEnum Class),
        RecursiveCacheEntry
    > _store = new(
        CacheName,
        key => key.Name,
        () => options.CurrentValue.MaxCacheEntries,
        // A TTL-expired entry is kept for this resolver's own stale-serve grace window (zero,
        // when RFC 8767 serve-stale is disabled entirely) -- TryGetStale needs it until then.
        () =>
            options.CurrentValue.EnableServeStale
                ? options.CurrentValue.MaxStaleServeAge
                : TimeSpan.Zero,
        eventPublisher,
        "expired-or-capacity"
    );

    /// <inheritdoc />
    public string Name => CacheName;

    /// <inheritdoc />
    public RecursiveCacheEntry TryGet(
        DnsName name,
        DnsResourceRecordTypeEnum type,
        DnsClassEnum recordClass
    ) =>
        _store.TryGetFresh((name, type, recordClass), out RecursiveCacheEntry entry) ? entry : null;

    /// <inheritdoc />
    public RecursiveCacheEntry TryGetStale(
        DnsName name,
        DnsResourceRecordTypeEnum type,
        DnsClassEnum recordClass,
        TimeSpan maxStaleAge
    ) =>
        _store.TryGetStale((name, type, recordClass), maxStaleAge, out RecursiveCacheEntry entry)
            ? entry
            : null;

    /// <inheritdoc />
    public void StorePositive(
        DnsName name,
        DnsResourceRecordTypeEnum type,
        DnsClassEnum recordClass,
        IReadOnlyList<DnsResourceRecord> records,
        IReadOnlyList<DnsResourceRecord> dnssecRecords
    )
    {
        if (records.Count == 0)
            return;

        uint ttlSeconds = records.Min(record => record.TimeToLive);

        _store.Set(
            (name, type, recordClass),
            new RecursiveCacheEntry(
                false,
                records,
                dnssecRecords,
                DnsResponseCodeEnum.NoError,
                null,
                _store.UtcNow.AddSeconds(ttlSeconds)
            )
        );
    }

    /// <inheritdoc />
    public void StoreNegative(
        DnsName name,
        DnsResourceRecordTypeEnum type,
        DnsClassEnum recordClass,
        DnsResponseCodeEnum responseCode,
        DnsResourceRecord soa,
        IReadOnlyList<DnsResourceRecord> dnssecRecords
    )
    {
        if (
            DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.Soa, soa.Data)
            is not SoaRecordData soaData
        )
            return;

        uint ttlSeconds = Math.Min(soa.TimeToLive, soaData.MinimumTtlSeconds);

        _store.Set(
            (name, type, recordClass),
            new RecursiveCacheEntry(
                true,
                [],
                dnssecRecords,
                responseCode,
                soa,
                _store.UtcNow.AddSeconds(ttlSeconds)
            )
        );
    }

    /// <inheritdoc cref="IRecursiveCache.EvictExpiredAndOverCapacity" />
    public int EvictExpiredAndOverCapacity() => _store.EvictExpiredAndOverCapacity();

    /// <inheritdoc />
    public DnsCacheStatistics GetStatistics() => _store.GetStatistics();

    /// <inheritdoc />
    public int Flush() => _store.Flush();

    /// <inheritdoc />
    public int Remove(DnsName name) => _store.RemoveName(name);

    /// <inheritdoc />
    public IReadOnlyList<(
        DnsName Name,
        DnsResourceRecordTypeEnum Type,
        DnsClassEnum Class
    )> GetPrefetchCandidates(TimeSpan prefetchWindow, TimeSpan recentAccessWindow)
    {
        DateTimeOffset now = _store.UtcNow;

        return
        [
            .. _store
                .Snapshot()
                .Where(item =>
                    !item.Entry.IsNegative
                    && now < item.Entry.ExpiresAtUtc
                    && item.Entry.ExpiresAtUtc - now <= prefetchWindow
                    && item.LastAccessedUtc is { } lastAccessed
                    && now - lastAccessed <= recentAccessWindow
                )
                .Select(item => item.Key),
        ];
    }
}
