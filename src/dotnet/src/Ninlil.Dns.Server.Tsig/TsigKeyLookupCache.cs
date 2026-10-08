using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Tsig;

/// <summary>
///     This class caches TSIG key lookups for a few seconds. Every signed message, from a flood of
///     unauthenticated ones to each message of a long AXFR, needs its key; without a cache each one
///     costs a database query, so a sender with a made-up key name could turn the key table into a
///     query amplifier. Unknown names are cached too (a negative entry), for the same reason.
///     <para>
///         The cache is a singleton shared by the scoped <see cref="TsigService" /> instances. A key
///         that an operator disables, deletes or changes keeps its old verdict for at most
///         <see cref="Lifetime" />.
///     </para>
/// </summary>
/// <param name="timeProvider">The clock that decides when an entry expires.</param>
public sealed class TsigKeyLookupCache(TimeProvider timeProvider)
{
    /// <summary>
    ///     This constant contains how long a key, or the absence of one, is remembered.
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     This constant contains the most entries kept; the oldest is dropped to make room.
    /// </summary>
    public const int MaximumEntries = 1024;

    private readonly object _lock = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    /// <summary>
    ///     This method returns the key with the given name, from the cache when a fresh entry
    ///     exists and from <paramref name="repository" /> otherwise. Key names are stored in
    ///     lowercase, but rows written before that rule may keep their case, so the canonical
    ///     lowercase name is tried first and the name as given second.
    /// </summary>
    /// <param name="name">The key name from the message, with or without the trailing dot.</param>
    /// <param name="repository">The repository to query on a cache miss.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The key, or null when no key has that name.</returns>
    public async Task<TsigKeyEntity> FindAsync(
        string name,
        ITsigKeyRepository repository,
        CancellationToken stoppingToken
    )
    {
        string exact = name.TrimEnd('.');
        string canonical = exact.ToLowerInvariant();
        DateTimeOffset now = timeProvider.GetUtcNow();

        lock (_lock)
        {
            if (_entries.TryGetValue(canonical, out Entry cached) && cached.ExpiresAt > now)
                return cached.Key;
        }

        TsigKeyEntity key = await repository.FindByNameAsync(canonical, stoppingToken);

        if (key is null && exact != canonical)
            key = await repository.FindByNameAsync(exact, stoppingToken);

        lock (_lock)
        {
            if (_entries.Count >= MaximumEntries && !_entries.ContainsKey(canonical))
                EvictOldest(now);

            _entries[canonical] = new Entry(key, now + Lifetime);
        }

        return key;
    }

    /// <summary>
    ///     This method removes expired entries, and the oldest one if none had expired. The caller
    ///     holds the lock. A linear scan is fine for 1 024 entries and only runs when the cache is
    ///     full.
    /// </summary>
    private void EvictOldest(DateTimeOffset now)
    {
        string oldest = null;
        DateTimeOffset oldestExpiry = DateTimeOffset.MaxValue;
        List<string> expired = [];

        foreach (KeyValuePair<string, Entry> pair in _entries)
        {
            if (pair.Value.ExpiresAt <= now)
                expired.Add(pair.Key);
            else if (pair.Value.ExpiresAt < oldestExpiry)
            {
                oldestExpiry = pair.Value.ExpiresAt;
                oldest = pair.Key;
            }
        }

        foreach (string key in expired)
            _entries.Remove(key);

        if (expired.Count == 0 && oldest is not null)
            _entries.Remove(oldest);
    }

    /// <summary>
    ///     This record holds one cached verdict: the key, or null for an unknown name.
    /// </summary>
    private sealed record Entry(TsigKeyEntity Key, DateTimeOffset ExpiresAt);
}
