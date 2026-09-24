using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Caching;

/// <summary>
///     This interface is the operator- and maintenance-facing side of every in-memory DNS answer
///     cache this server keeps: the recursive resolver's RRset cache and the forwarded-response
///     cache. Each cache registers itself under this interface as well as its own
///     resolver-facing one, so the periodic sweep (<see cref="DnsCacheEvictionService" />) and the
///     REST endpoint that shows, flushes and trims caches can treat every cache alike without
///     knowing what shape of entry each one holds.
///     <para>
///         Every cache is per process, and so per cluster node: answers are never replicated,
///         and flushing a cache on one node leaves the other nodes' caches alone. That is the
///         usual behaviour for a resolver cache and keeps the query path free of any network
///         coordination.
///     </para>
/// </summary>
public interface IDnsCache
{
    /// <summary>
    ///     This property contains the cache's own short, stable name, for example
    ///     <c>forwarded</c> or <c>recursive</c>. The REST API uses it to address one cache.
    /// </summary>
    string Name { get; }

    /// <summary>
    ///     This method reports the cache's current size and hit rate.
    /// </summary>
    /// <returns>A point-in-time snapshot.</returns>
    DnsCacheStatistics GetStatistics();

    /// <summary>
    ///     This method empties the cache and resets its hit and miss counters.
    /// </summary>
    /// <returns>How many entries were removed.</returns>
    int Flush();

    /// <summary>
    ///     This method removes every entry cached for exactly <paramref name="name" /> (every
    ///     type and class, and for the forwarded cache every upstream set), compared
    ///     case-insensitively. Names below it are left alone.
    /// </summary>
    /// <param name="name">The owner name to forget.</param>
    /// <returns>How many entries were removed.</returns>
    int Remove(DnsName name);

    /// <summary>
    ///     This method removes expired entries (once they are past any serve-stale window) and
    ///     trims the cache back to its size bound, soonest-to-expire first.
    /// </summary>
    /// <returns>How many entries were evicted.</returns>
    int EvictExpiredAndOverCapacity();
}
