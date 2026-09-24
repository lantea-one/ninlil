using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Caching;

/// <summary>
///     This class implements <see cref="IRecursiveCache" /> with a single, thread-safe,
///     in-memory dictionary.
/// </summary>
/// <param name="options">
///     This cache's own capacity limit, read live via <see cref="IOptionsMonitor{TOptions}" /> --
///     see <see cref="Resolution.RecursiveQueryResolver" />'s own doc comment for why this cache,
///     registered as a singleton, cannot use the usual snapshotted <see cref="IOptions{TOptions}" />
///     instead.
/// </param>
/// <param name="eventPublisher">The publisher this cache reports its own evictions through.</param>
public sealed class RecursiveCache(
    IOptionsMonitor<RecursiveServerOptions> options,
    IDnsServerEventPublisher eventPublisher
) : IRecursiveCache
{
    private readonly ConcurrentDictionary<
        (DnsName Name, DnsResourceRecordTypeEnum Type, DnsClassEnum Class),
        RecursiveCacheEntry
    > _entries = new();

    /// <summary>
    ///     This field tracks when each entry was last actually looked up (a real hit, fresh or
    ///     stale), independent of <see cref="_entries" /> itself -- the "is this name still being
    ///     queried" signal <see cref="GetPrefetchCandidates" /> needs, and that a
    ///     <see cref="RecursiveCacheEntry" /> record's own immutable shape has no room for without
    ///     turning every write into a full replace on every single read.
    /// </summary>
    private readonly ConcurrentDictionary<
        (DnsName Name, DnsResourceRecordTypeEnum Type, DnsClassEnum Class),
        DateTimeOffset
    > _lastAccessedUtc = new();

    /// <inheritdoc />
    public RecursiveCacheEntry TryGet(
        DnsName name,
        DnsResourceRecordTypeEnum type,
        DnsClassEnum recordClass
    )
    {
        (DnsName, DnsResourceRecordTypeEnum, DnsClassEnum) key = (name, type, recordClass);

        if (_entries.TryGetValue(key, out RecursiveCacheEntry entry) && !entry.IsExpired)
        {
            _lastAccessedUtc[key] = DateTimeOffset.UtcNow;
            return entry;
        }

        return null;
    }

    /// <inheritdoc />
    public RecursiveCacheEntry TryGetStale(
        DnsName name,
        DnsResourceRecordTypeEnum type,
        DnsClassEnum recordClass,
        TimeSpan maxStaleAge
    )
    {
        (DnsName, DnsResourceRecordTypeEnum, DnsClassEnum) key = (name, type, recordClass);

        if (
            !_entries.TryGetValue(key, out RecursiveCacheEntry entry)
            || DateTimeOffset.UtcNow >= entry.ExpiresAtUtc + maxStaleAge
        )
            return null;

        _lastAccessedUtc[key] = DateTimeOffset.UtcNow;
        return entry;
    }

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

        _entries[(name, type, recordClass)] = new RecursiveCacheEntry(
            false,
            records,
            dnssecRecords,
            DnsResponseCodeEnum.NoError,
            null,
            DateTimeOffset.UtcNow.AddSeconds(ttlSeconds)
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

        _entries[(name, type, recordClass)] = new RecursiveCacheEntry(
            true,
            [],
            dnssecRecords,
            responseCode,
            soa,
            DateTimeOffset.UtcNow.AddSeconds(ttlSeconds)
        );
    }

    /// <inheritdoc />
    public int EvictExpiredAndOverCapacity()
    {
        int evictedCount = 0;

        // A TTL-expired entry is only actually removed once it has also aged past this
        // resolver's own configured stale-serve grace window (zero, when RFC 8767 serve-stale is
        // disabled entirely) -- TryGetStale's own future callers need it to still be here until
        // then. Over-capacity pressure below still evicts an entry regardless of that grace
        // window; a hard memory ceiling takes priority over stale-serve reach.
        TimeSpan staleRetention = options.CurrentValue.EnableServeStale
            ? options.CurrentValue.MaxStaleServeAge
            : TimeSpan.Zero;
        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (
            var pair in _entries
                .Where(pair => now >= pair.Value.ExpiresAtUtc + staleRetention)
                .ToList()
        )
            if (_entries.TryRemove(pair.Key, out _))
            {
                _lastAccessedUtc.TryRemove(pair.Key, out _);
                evictedCount++;
            }

        int overCapacityCount = _entries.Count - options.CurrentValue.MaxCacheEntries;

        if (overCapacityCount > 0)
        {
            // Evict whichever remaining entries expire soonest first -- a simple, if not
            // perfectly LRU, approximation of "least likely to still be useful."
            List<(DnsName Name, DnsResourceRecordTypeEnum Type, DnsClassEnum Class)> keysToEvict =
            [
                .. _entries
                    .OrderBy(pair => pair.Value.ExpiresAtUtc)
                    .Take(overCapacityCount)
                    .Select(pair => pair.Key),
            ];

            foreach (var key in keysToEvict)
                if (_entries.TryRemove(key, out _))
                {
                    _lastAccessedUtc.TryRemove(key, out _);
                    evictedCount++;
                }
        }

        if (evictedCount > 0)
            eventPublisher.Publish(
                new DnsCacheEvictedEvent(DateTimeOffset.UtcNow, "expired-or-capacity", evictedCount)
            );

        return evictedCount;
    }

    /// <inheritdoc />
    public IReadOnlyList<(
        DnsName Name,
        DnsResourceRecordTypeEnum Type,
        DnsClassEnum Class
    )> GetPrefetchCandidates(TimeSpan prefetchWindow, TimeSpan recentAccessWindow)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        return
        [
            .. _entries
                .Where(pair =>
                    !pair.Value.IsNegative
                    && !pair.Value.IsExpired
                    && pair.Value.ExpiresAtUtc - now <= prefetchWindow
                    && _lastAccessedUtc.TryGetValue(pair.Key, out DateTimeOffset lastAccessed)
                    && now - lastAccessed <= recentAccessWindow
                )
                .Select(pair => pair.Key),
        ];
    }
}
