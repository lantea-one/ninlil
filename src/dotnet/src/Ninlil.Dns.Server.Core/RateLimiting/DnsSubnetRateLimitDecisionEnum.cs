// Define our namespace.
namespace Ninlil.Dns.Server.Core.RateLimiting;

/// <summary>
///     This enumeration lists the outcomes of <see cref="IDnsRateLimiter.CheckSubnetLimitAsync" />.
///     Unlike the per-IP check, which only allows or drops, a UDP query over its subnet's limit
///     can get a middle response: a truncated reply that sends the client to TCP.
/// </summary>
public enum DnsSubnetRateLimitDecisionEnum
{
    /// <summary>The query proceeds normally.</summary>
    Allow,

    /// <summary>
    ///     The query is answered with the TC (truncated) bit set and no content, so a real client
    ///     retries over TCP, which cannot be spoofed or used for amplification. Only returned for
    ///     UDP queries (see the <c>isUdp</c> parameter of
    ///     <see cref="IDnsRateLimiter.CheckSubnetLimitAsync" />).
    /// </summary>
    RespondTruncated,

    /// <summary>
    ///     The query is dropped without a reply, as when <see cref="IDnsRateLimiter.TryAcquireAsync" />
    ///     returns <see langword="false" />.
    /// </summary>
    Drop,
}
