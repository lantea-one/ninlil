// Define our namespace.
namespace Ninlil.Dns.Server.RateLimiting;

/// <summary>
///     This class binds to the <c>DnsServer:RateLimiting</c> section of the application's
///     configuration and controls <see cref="TokenBucketDnsRateLimiter" />'s own behavior.
/// </summary>
public sealed class DnsRateLimitOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:RateLimiting";

    /// <summary>
    ///     This property controls whether rate limiting runs at all. Off by default: unlike DNS
    ///     Cookie support, an overly strict limit can genuinely cost a legitimate client its own
    ///     availability (most visibly, many real users sharing one IPv4 address behind CGNAT or
    ///     a household's own router), which is a real behavior change an operator should opt
    ///     into rather than inherit silently.
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    ///     This property contains how many queries per second a single client IP is sustained-
    ///     allowed to send, once its own burst allowance (<see cref="BurstSize" />) is spent.
    /// </summary>
    public double QueriesPerSecond { get; set; } = 20;

    /// <summary>
    ///     This property contains the largest number of queries a single client IP may send in
    ///     an instantaneous burst before <see cref="QueriesPerSecond" />'s own steady-state rate
    ///     takes over -- the token bucket's own capacity.
    /// </summary>
    public int BurstSize { get; set; } = 40;

    /// <summary>
    ///     This property contains how long a client IP's own tracked bucket is kept once that
    ///     client stops querying, before <see cref="DnsRateLimitCleanupService" /> reclaims it.
    ///     Set generously above any realistic query gap so a client is never seen as "new" (and
    ///     handed a full fresh burst allowance) simply because it paused briefly.
    /// </summary>
    public TimeSpan TrackedClientExpiry { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     This property contains how often <see cref="DnsRateLimitCleanupService" /> sweeps for
    ///     buckets older than <see cref="TrackedClientExpiry" />.
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    ///     This property contains the largest number of distinct client IPs this limiter ever
    ///     tracks a bucket for at once. A source address that would exceed this ceiling is
    ///     allowed through unthrottled rather than denied, since a limiter that itself becomes a
    ///     resource-exhaustion target (an attacker cycling through spoofed source addresses
    ///     specifically to grow this table) must fail open, not open a second, unbounded-memory
    ///     denial-of-service vector of its own. A known, accepted limitation, not a full
    ///     mitigation.
    /// </summary>
    public int MaxTrackedClients { get; set; } = 100_000;

    /// <summary>
    ///     This property contains how many leading bits of a querying IPv4 client's own address
    ///     <see cref="TokenBucketDnsRateLimiter.CheckSubnetLimitAsync" /> groups together into one
    ///     subnet-level bucket. Defaults to <c>24</c> -- an ordinary, publicly-documented default
    ///     bucket granularity comparable DNS servers already use for this exact kind of check, not
    ///     anyone's proprietary code.
    /// </summary>
    public byte SubnetPrefixLengthIPv4 { get; set; } = 24;

    /// <summary>
    ///     This property contains the IPv6 counterpart to <see cref="SubnetPrefixLengthIPv4" />.
    ///     Defaults to <c>56</c> -- the conventional IPv6 analogue (a /56 typically corresponds to
    ///     one residential/small-site delegation) comparable DNS servers already use.
    /// </summary>
    public byte SubnetPrefixLengthIPv6 { get; set; } = 56;

    /// <summary>
    ///     This property contains how long a subnet's own query counts accumulate before resetting
    ///     to zero and starting fresh. Defaults to 10 seconds -- short enough that this check is
    ///     reacting to an actual live burst rather than a slow accumulation over minutes (which
    ///     <see cref="TokenBucketDnsRateLimiter.TryAcquireAsync" />'s own steady-state per-IP budget already governs at a
    ///     finer grain), long enough to distinguish a real burst from ordinary momentary query
    ///     clustering across a subnet's own many real, independent clients.
    /// </summary>
    public TimeSpan SubnetSampleWindow { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     This property contains how many UDP queries a single subnet bucket may receive within
    ///     <see cref="SubnetSampleWindow" /> before further UDP queries from it become eligible for
    ///     <see cref="SubnetTruncatePercentage" />'s own graduated response. Defaults to 500 --
    ///     deliberately generous relative to a single client's own 40-query burst allowance
    ///     (<see cref="BurstSize" />), since a real subnet (a household, small office, or a whole
    ///     site behind CGNAT) may plausibly contain dozens of simultaneously active, entirely
    ///     legitimate clients; this check exists to catch a genuine flood, not to throttle ordinary
    ///     shared-network query volume.
    /// </summary>
    public int SubnetUdpQueryLimit { get; set; } = 500;

    /// <summary>
    ///     This property contains how many TCP queries a single subnet bucket may receive within
    ///     <see cref="SubnetSampleWindow" /> before further TCP queries from it are dropped
    ///     outright -- no graduated response applies to TCP (see
    ///     <see cref="Core.RateLimiting.DnsSubnetRateLimitDecisionEnum.RespondTruncated" />'s own
    ///     doc comment for why). Defaults to 100, lower than <see cref="SubnetUdpQueryLimit" />
    ///     since real TCP DNS traffic (zone transfers aside, which this limiter does not gate at
    ///     all -- see <c>DnsQueryProcessingPipeline</c>) is ordinarily a small fraction of a
    ///     subnet's overall query volume, so a much higher share of it reaching this limit at all
    ///     is itself a stronger signal.
    /// </summary>
    public int SubnetTcpQueryLimit { get; set; } = 100;

    /// <summary>
    ///     This property contains what percentage (0-100) of a subnet's own over-limit UDP queries
    ///     are answered with a truncated response (forcing a retry over TCP) rather than dropped
    ///     outright. Defaults to 50 -- a deliberate middle ground: high enough that a real,
    ///     legitimately large subnet genuinely over its own limit still gets a real chance to
    ///     succeed via a TCP retry rather than simply failing outright, low enough that this
    ///     server does not itself become a reflection amplifier by unconditionally answering every
    ///     single over-limit query (even a spoofed one) with a real outbound packet.
    /// </summary>
    public double SubnetTruncatePercentage { get; set; } = 50;

    /// <summary>
    ///     This property contains CIDR entries (e.g. <c>10.0.0.0/8</c>) exempt from
    ///     <see cref="TokenBucketDnsRateLimiter.CheckSubnetLimitAsync" /> entirely -- checked
    ///     before a subnet bucket is ever looked up or created for a matching client, mirroring
    ///     <see cref="TokenBucketDnsRateLimiter.TryAcquireAsync" />'s own access-control-engine exemption. Empty by default
    ///     (nothing exempted).
    /// </summary>
    public List<string> SubnetBypassCidrs { get; set; } = [];
}
