// Define our namespace.
namespace Ninlil.Dns.Server.Core.RateLimiting;

/// <summary>
///     This enumeration lists the three outcomes <see cref="IDnsRateLimiter.CheckSubnetLimitAsync" />
///     can reach, distinct from <see cref="IDnsRateLimiter.TryAcquireAsync" />'s own plain
///     <see langword="bool" /> -- a subnet-level decision needs a third state a single client's
///     own exact-IP budget never did, since a UDP query over its subnet's own limit gets a
///     graduated response rather than being either allowed outright or dropped outright.
/// </summary>
public enum DnsSubnetRateLimitDecisionEnum
{
    /// <summary>The query proceeds normally.</summary>
    Allow,

    /// <summary>
    ///     The query is answered with the TC (truncated) bit set and no real content -- forcing a
    ///     UDP client to retry over TCP, which is inherently harder to spoof/amplify than the UDP
    ///     transport this decision is reached over in the first place. Never returned for a query
    ///     that already arrived over a reliable transport (see <see cref="IDnsRateLimiter.CheckSubnetLimitAsync" />'s
    ///     own <c>isUdp</c> parameter) -- retrying "over TCP" is meaningless advice to a client
    ///     already using it.
    /// </summary>
    RespondTruncated,

    /// <summary>
    ///     The query is dropped silently, exactly like <see cref="IDnsRateLimiter.TryAcquireAsync" />'s
    ///     own <see langword="false" /> result -- no response at all.
    /// </summary>
    Drop,
}
