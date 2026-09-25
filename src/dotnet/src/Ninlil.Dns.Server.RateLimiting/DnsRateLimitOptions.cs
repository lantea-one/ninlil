// Define our namespace.
namespace Ninlil.Dns.Server.RateLimiting;

/// <summary>
///     This class binds to the <c>DnsServer:RateLimiting</c> section of the application's
///     configuration and controls <see cref="TokenBucketDnsRateLimiter" />.
///     <c>RateLimitingSettingsController</c> writes it; a change takes effect after a restart.
/// </summary>
public sealed class DnsRateLimitOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:RateLimiting";

    /// <summary>
    ///     This property controls whether rate limiting runs at all. It is off by default: a limit
    ///     that is too strict can lock out legitimate clients (for example many users sharing one
    ///     IPv4 address behind CGNAT or a home router), so an operator should opt in.
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    ///     This property contains the sustained number of queries per second one client IP may
    ///     send once its burst allowance (<see cref="BurstSize" />) is spent.
    /// </summary>
    public double QueriesPerSecond { get; set; } = 20;

    /// <summary>
    ///     This property contains the largest number of queries one client IP may send in a burst
    ///     before the <see cref="QueriesPerSecond" /> rate applies. It is the token bucket's
    ///     capacity.
    /// </summary>
    public int BurstSize { get; set; } = 40;

    /// <summary>
    ///     This property contains how long a client IP's bucket (and a subnet's window) is kept
    ///     after the last query, before <see cref="DnsRateLimitCleanupService" /> removes it. It is
    ///     set well above normal query gaps so a client that pauses briefly is not treated as new
    ///     and given a fresh burst allowance.
    /// </summary>
    public TimeSpan TrackedClientExpiry { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     This property contains how often <see cref="DnsRateLimitCleanupService" /> sweeps for
    ///     buckets older than <see cref="TrackedClientExpiry" />.
    /// </summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    ///     This property contains the largest number of client IPs the limiter tracks at once. The
    ///     same ceiling applies separately to the number of tracked subnets. A new address (or
    ///     subnet) beyond the ceiling is let through unthrottled rather than denied: the limit
    ///     keeps an attacker cycling through spoofed source addresses from growing the table
    ///     without bound, at the cost of not limiting those extra addresses.
    /// </summary>
    public int MaxTrackedClients { get; set; } = 100_000;

    /// <summary>
    ///     This property contains how many leading bits of an IPv4 client address
    ///     <see cref="TokenBucketDnsRateLimiter.CheckSubnetLimitAsync" /> uses to group clients into
    ///     one subnet window. The default, <c>24</c>, is the usual granularity for this kind of
    ///     check in other DNS servers.
    /// </summary>
    public byte SubnetPrefixLengthIPv4 { get; set; } = 24;

    /// <summary>
    ///     This property contains the IPv6 counterpart to <see cref="SubnetPrefixLengthIPv4" />.
    ///     The default, <c>56</c>, is the usual IPv6 choice, since a /56 is a typical delegation to
    ///     one home or small site.
    /// </summary>
    public byte SubnetPrefixLengthIPv6 { get; set; } = 56;

    /// <summary>
    ///     This property contains how long a subnet's query counts accumulate before they reset to
    ///     zero. The default of 10 seconds is short enough to react to a live burst rather than
    ///     slow accumulation (the per-IP budget in
    ///     <see cref="TokenBucketDnsRateLimiter.TryAcquireAsync" /> covers that), and long enough
    ///     not to react to brief clusters of queries from the many clients in one subnet.
    /// </summary>
    public TimeSpan SubnetSampleWindow { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     This property contains how many UDP queries a single subnet bucket may receive within
    ///     <see cref="SubnetSampleWindow" /> before further UDP queries from it are truncated or
    ///     dropped (see <see cref="SubnetTruncatePercentage" />). The default of 500 is generous
    ///     next to one client's burst of 40 (<see cref="BurstSize" />), since a subnet (a home, a
    ///     small office, a site behind CGNAT) can hold dozens of active clients; the check is
    ///     meant to catch a flood, not normal shared-network traffic.
    /// </summary>
    public int SubnetUdpQueryLimit { get; set; } = 500;

    /// <summary>
    ///     This property contains how many TCP queries a single subnet bucket may receive within
    ///     <see cref="SubnetSampleWindow" /> before further TCP queries from it are dropped. TCP
    ///     gets no truncated response, since the client is already on TCP (see
    ///     <see cref="Core.RateLimiting.DnsSubnetRateLimitDecisionEnum.RespondTruncated" />). The
    ///     default of 100 is lower than <see cref="SubnetUdpQueryLimit" /> because TCP is normally
    ///     a small share of a subnet's DNS traffic, so reaching the limit is a stronger signal.
    /// </summary>
    public int SubnetTcpQueryLimit { get; set; } = 100;

    /// <summary>
    ///     This property contains the percentage (0-100) of a subnet's over-limit UDP queries that
    ///     get a truncated response (sending the client to TCP) instead of being dropped. The
    ///     default of 50 is a middle ground: a busy but legitimate subnet still has a good chance
    ///     to get through over TCP, while the server does not answer every over-limit query,
    ///     spoofed ones included, and so is less useful as a reflector.
    /// </summary>
    public double SubnetTruncatePercentage { get; set; } = 50;

    /// <summary>
    ///     This property contains CIDR entries (e.g. <c>10.0.0.0/8</c>) exempt from
    ///     <see cref="TokenBucketDnsRateLimiter.CheckSubnetLimitAsync" />. A matching client never
    ///     gets a subnet window. It does not exempt the per-IP limit; an access-control rule with
    ///     the rate-limit bypass capability exempts both. Empty by default.
    /// </summary>
    public List<string> SubnetBypassCidrs { get; set; } = [];
}
