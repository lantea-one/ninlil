using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Caching;

/// <summary>
///     This interface is the resolver-facing side of the forwarded-response cache: what
///     <see cref="ForwarderQueryResolver" /> asks before going upstream and tells after an
///     upstream answered. Its operator-facing side (statistics, flush, per-name removal) is
///     <see cref="Ninlil.Dns.Server.Core.Caching.IDnsCache" />, which the same instance also
///     implements.
/// </summary>
public interface IForwardedResponseCache
{
    /// <summary>
    ///     This method answers <paramref name="request" /> from a still-fresh cached answer, with
    ///     TTLs reduced by the time already spent in the cache.
    /// </summary>
    /// <param name="key">The question's cache key.</param>
    /// <param name="request">The client's request, whose ID and question the answer carries.</param>
    /// <returns>A new response message, or <see langword="null" /> on a miss.</returns>
    DnsMessage TryGet(ForwardedResponseCacheKey key, DnsMessage request);

    /// <summary>
    ///     This method answers <paramref name="request" /> from an expired cached answer, when
    ///     RFC 8767 serve-stale is enabled and the answer is still within its stale window.
    ///     <see cref="ForwarderQueryResolver" /> asks only once every forwarder has failed.
    /// </summary>
    /// <param name="key">The question's cache key.</param>
    /// <param name="request">The client's request, whose ID and question the answer carries.</param>
    /// <returns>A new response message, or <see langword="null" /> when nothing usable is cached.</returns>
    DnsMessage TryGetStale(ForwardedResponseCacheKey key, DnsMessage request);

    /// <summary>
    ///     This method caches an upstream answer, if it is cacheable at all (see
    ///     <see cref="ForwardedResponseCache.ComputeLifetime" />).
    /// </summary>
    /// <param name="key">The question's cache key.</param>
    /// <param name="response">The upstream answer, exactly as received. It is copied, not kept.</param>
    /// <returns>Whether the answer was cached.</returns>
    bool TryStore(ForwardedResponseCacheKey key, DnsMessage response);
}
