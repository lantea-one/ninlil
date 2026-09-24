using System.Collections.Concurrent;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Caching;

/// <summary>
///     This class is the storage engine shared by every DNS answer cache in this server: a
///     thread-safe, in-memory, TTL-bound and size-bound dictionary. The recursive resolver's
///     RRset cache and the forwarded-response cache differ in what they key on and what an
///     entry holds, but expiry, serve-stale retention, the size bound, eviction events, hit and
///     miss counting, flushing and per-name removal are the same problem for both, so they are
///     solved once, here, rather than twice in two caches that could drift apart.
///     <para>
///         <b>Size bound.</b> The periodic sweep (<see cref="DnsCacheEvictionService" />) trims
///         the cache to exactly its bound once a minute. Between sweeps a burst of new names
///         could otherwise grow it without limit, so every 64th store also checks the size, and
///         trims straight away once the cache has grown more than 10% past its bound. Checking
///         only every 64th store keeps <see cref="ConcurrentDictionary{TKey,TValue}.Count" />
///         (which briefly locks the whole dictionary) off almost every store.
///     </para>
/// </summary>
/// <typeparam name="TKey">The cache key; its own equality decides what counts as "the same question".</typeparam>
/// <typeparam name="TEntry">The cached value.</typeparam>
/// <param name="cacheName">The owning cache's short name, used in eviction event reasons.</param>
/// <param name="nameOf">Returns the owner name a key is for, so an operator can remove one name.</param>
/// <param name="maxEntries">Returns the current size bound, read live so a settings change applies without a restart.</param>
/// <param name="staleRetention">
///     Returns how long past expiry an entry is kept for serve-stale (zero when serve-stale is
///     off), read live for the same reason.
/// </param>
/// <param name="eventPublisher">The publisher eviction events go out through.</param>
/// <param name="sweepEvictionReason">
///     The <see cref="DnsCacheEvictedEvent.Reason" /> for an expiry or size eviction. The
///     recursive cache keeps its long-standing <c>expired-or-capacity</c> so existing webhook
///     and broker consumers see no change.
/// </param>
/// <param name="timeProvider">The clock, replaceable in tests; the system clock by default.</param>
public sealed class DnsCacheStore<TKey, TEntry>(
    string cacheName,
    Func<TKey, DnsName> nameOf,
    Func<int> maxEntries,
    Func<TimeSpan> staleRetention,
    IDnsServerEventPublisher eventPublisher,
    string sweepEvictionReason,
    TimeProvider timeProvider = null
)
    where TKey : notnull
    where TEntry : class, IDnsCacheEntry
{
    /// <summary>
    ///     This constant contains how many stores pass between two size checks -- see this
    ///     class's own doc comment.
    /// </summary>
    private const int StoresPerSizeCheck = 64;

    private readonly ConcurrentDictionary<TKey, TEntry> _entries = new();

    /// <summary>
    ///     This field tracks when each entry was last actually looked up (a real hit, fresh or
    ///     stale), separately from the entry itself, so a read never has to replace an entry
    ///     just to record that it happened. The recursive cache's prefetch uses it to tell a
    ///     name that is still being asked for from one nobody wants any more.
    /// </summary>
    private readonly ConcurrentDictionary<TKey, DateTimeOffset> _lastAccessedUtc = new();

    /// <summary>
    ///     This field serializes eviction passes, so the periodic sweep and a store-triggered
    ///     trim never both sort and trim the same dictionary at once.
    /// </summary>
    private readonly Lock _evictionLock = new();

    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    private long _hits;
    private long _misses;
    private int _storesSinceSizeCheck;

    /// <summary>
    ///     This property contains the current UTC time on this store's own clock, so an owning
    ///     cache computing an expiry uses the same clock expiry is checked against.
    /// </summary>
    public DateTimeOffset UtcNow => _timeProvider.GetUtcNow();

    /// <summary>
    ///     This property contains how many entries the store holds right now, stale ones included.
    /// </summary>
    public int Count => _entries.Count;

    /// <summary>
    ///     This method reports this store's size and hit rate as <see cref="DnsCacheStatistics" />.
    /// </summary>
    public DnsCacheStatistics GetStatistics() =>
        new(
            cacheName,
            _entries.Count,
            maxEntries(),
            Interlocked.Read(ref _hits),
            Interlocked.Read(ref _misses)
        );

    /// <summary>
    ///     This method looks up a still-fresh entry, counting the lookup as a hit or a miss.
    /// </summary>
    /// <param name="key">The key to look up.</param>
    /// <param name="entry">The fresh entry, or <see langword="null" />.</param>
    /// <returns>Whether a fresh entry was found.</returns>
    public bool TryGetFresh(TKey key, out TEntry entry)
    {
        DateTimeOffset now = UtcNow;

        if (_entries.TryGetValue(key, out entry) && now < entry.ExpiresAtUtc)
        {
            _lastAccessedUtc[key] = now;
            Interlocked.Increment(ref _hits);
            return true;
        }

        entry = null;
        Interlocked.Increment(ref _misses);
        return false;
    }

    /// <summary>
    ///     This method looks up an entry whether or not it has expired, as long as it is still
    ///     within <paramref name="maxStaleAge" /> of its expiry (RFC 8767 serve-stale). It is not
    ///     counted as a hit or a miss: a caller only asks after a fresh lookup already missed and
    ///     upstream then failed, and that miss was already counted.
    /// </summary>
    /// <param name="key">The key to look up.</param>
    /// <param name="maxStaleAge">How far past expiry an entry may still be returned.</param>
    /// <param name="entry">The entry, or <see langword="null" />.</param>
    /// <returns>Whether a usable entry was found.</returns>
    public bool TryGetStale(TKey key, TimeSpan maxStaleAge, out TEntry entry)
    {
        DateTimeOffset now = UtcNow;

        if (!_entries.TryGetValue(key, out entry) || now >= entry.ExpiresAtUtc + maxStaleAge)
        {
            entry = null;
            return false;
        }

        _lastAccessedUtc[key] = now;
        return true;
    }

    /// <summary>
    ///     This method stores an entry, replacing whatever was cached under the same key, then
    ///     trims the store if it has grown well past its bound (see this class's own doc comment).
    /// </summary>
    /// <param name="key">The key to store under.</param>
    /// <param name="entry">The entry to store.</param>
    public void Set(TKey key, TEntry entry)
    {
        _entries[key] = entry;

        if (Interlocked.Increment(ref _storesSinceSizeCheck) % StoresPerSizeCheck != 0)
            return;

        int bound = maxEntries();

        if (_entries.Count > bound + Math.Max(1, bound / 10))
            EvictExpiredAndOverCapacity();
    }

    /// <summary>
    ///     This method returns every entry alongside when it was last looked up (or
    ///     <see langword="null" /> if it never was), for an owning cache that needs to scan its
    ///     own entries, such as the recursive cache's prefetch.
    /// </summary>
    public IEnumerable<(TKey Key, TEntry Entry, DateTimeOffset? LastAccessedUtc)> Snapshot() =>
        _entries.Select(pair =>
            (
                pair.Key,
                pair.Value,
                _lastAccessedUtc.TryGetValue(pair.Key, out DateTimeOffset lastAccessed)
                    ? lastAccessed
                    : (DateTimeOffset?)null
            )
        );

    /// <summary>
    ///     This method removes every expired entry (once past the stale-retention window) and
    ///     then trims to the size bound, soonest-to-expire first, publishing one
    ///     <see cref="DnsCacheEvictedEvent" /> when anything was removed.
    /// </summary>
    /// <returns>How many entries were evicted.</returns>
    public int EvictExpiredAndOverCapacity()
    {
        lock (_evictionLock)
        {
            int evictedCount = 0;

            // A TTL-expired entry is only actually removed once it has also aged past the
            // stale-serve grace window (zero when serve-stale is off) -- a later serve-stale
            // lookup needs it to still be here until then. The size bound below still evicts an
            // entry regardless of that window; a hard memory ceiling takes priority over how far
            // back stale answers can reach.
            TimeSpan retention = staleRetention();
            DateTimeOffset now = UtcNow;

            foreach (
                TKey key in _entries
                    .Where(pair => now >= pair.Value.ExpiresAtUtc + retention)
                    .Select(pair => pair.Key)
                    .ToList()
            )
                if (TryRemove(key))
                    evictedCount++;

            int overCapacityCount = _entries.Count - maxEntries();

            if (overCapacityCount > 0)
            {
                // Evict whichever remaining entries expire soonest first -- a simple, if not
                // perfectly LRU, approximation of "least likely to still be useful."
                List<TKey> keysToEvict =
                [
                    .. _entries
                        .OrderBy(pair => pair.Value.ExpiresAtUtc)
                        .Take(overCapacityCount)
                        .Select(pair => pair.Key),
                ];

                foreach (TKey key in keysToEvict)
                    if (TryRemove(key))
                        evictedCount++;
            }

            if (evictedCount > 0)
                eventPublisher.Publish(
                    new DnsCacheEvictedEvent(UtcNow, sweepEvictionReason, evictedCount)
                );

            return evictedCount;
        }
    }

    /// <summary>
    ///     This method empties the store and resets its hit and miss counters, so statistics
    ///     read after a flush describe only what happened since.
    /// </summary>
    /// <returns>How many entries were removed.</returns>
    public int Flush()
    {
        int removedCount;

        lock (_evictionLock)
        {
            removedCount = _entries.Count;
            _entries.Clear();
            _lastAccessedUtc.Clear();
            Interlocked.Exchange(ref _hits, 0);
            Interlocked.Exchange(ref _misses, 0);
        }

        if (removedCount > 0)
            eventPublisher.Publish(
                new DnsCacheEvictedEvent(UtcNow, $"{cacheName}-flushed", removedCount)
            );

        return removedCount;
    }

    /// <summary>
    ///     This method removes every entry whose key is for exactly <paramref name="name" />.
    /// </summary>
    /// <param name="name">The owner name to forget; compared case-insensitively.</param>
    /// <returns>How many entries were removed.</returns>
    public int RemoveName(DnsName name)
    {
        int removedCount = 0;

        foreach (TKey key in _entries.Keys.Where(key => nameOf(key) == name).ToList())
            if (TryRemove(key))
                removedCount++;

        if (removedCount > 0)
            eventPublisher.Publish(
                new DnsCacheEvictedEvent(UtcNow, $"{cacheName}-removed", removedCount)
            );

        return removedCount;
    }

    private bool TryRemove(TKey key)
    {
        if (!_entries.TryRemove(key, out _))
            return false;

        _lastAccessedUtc.TryRemove(key, out _);
        return true;
    }
}
