// Define our namespace.
namespace Ninlil.Dns.Server.Core.Caching;

/// <summary>
///     This record is a point-in-time snapshot of one <see cref="IDnsCache" />'s size and hit
///     rate, for the operator-facing cache endpoint.
/// </summary>
/// <param name="Name">The cache's own short name, for example <c>forwarded</c> or <c>recursive</c>.</param>
/// <param name="EntryCount">How many entries the cache holds right now, stale ones included.</param>
/// <param name="MaxEntries">The configured size bound the cache is trimmed back to.</param>
/// <param name="Hits">How many lookups found a fresh entry since this process started or the cache was last flushed.</param>
/// <param name="Misses">How many lookups found nothing fresh over the same period.</param>
public record DnsCacheStatistics(
    string Name,
    int EntryCount,
    int MaxEntries,
    long Hits,
    long Misses
);
