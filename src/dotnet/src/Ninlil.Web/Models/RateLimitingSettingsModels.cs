// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST shape of the DNS rate limiting settings (<c>DnsRateLimitOptions</c>)
///     that <c>GET /api/rate-limiting-settings</c> returns. It covers the per-client token bucket
///     and the per-subnet response limits. A <c>PATCH</c> to the same endpoint accepts every
///     field except the last two, which are read-only cluster sync bookkeeping.
/// </summary>
/// <param name="Enable">Whether rate limiting is applied. Defaults to <see langword="false" />.</param>
/// <param name="QueriesPerSecond">The steady query rate allowed per client. Defaults to 20.</param>
/// <param name="BurstSize">How many queries a client may send at once above the steady rate. Defaults to 40.</param>
/// <param name="TrackedClientExpiry">How long an idle client's bucket, or a subnet's window, is kept. Defaults to 5 minutes.</param>
/// <param name="CleanupInterval">How often expired buckets are removed. Defaults to 1 minute.</param>
/// <param name="MaxTrackedClients">The most client addresses, and separately subnets, tracked at once; beyond it new ones go unthrottled. Defaults to 100,000.</param>
/// <param name="SubnetPrefixLengthIPv4">The IPv4 prefix length clients are grouped by for subnet limits. Defaults to 24.</param>
/// <param name="SubnetPrefixLengthIPv6">The IPv6 prefix length clients are grouped by for subnet limits. Defaults to 56.</param>
/// <param name="SubnetSampleWindow">The window subnet query counts are measured over. Defaults to 10 seconds.</param>
/// <param name="SubnetUdpQueryLimit">How many UDP queries a subnet may send per window. Defaults to 500.</param>
/// <param name="SubnetTcpQueryLimit">How many TCP queries a subnet may send per window. Defaults to 100.</param>
/// <param name="SubnetTruncatePercentage">The percentage of over-limit UDP queries answered with a truncated response instead of dropped. Defaults to 50.</param>
/// <param name="SubnetBypassCidrs">CIDR networks the subnet limits never apply to (the per-client limit still does). Empty by default.</param>
/// <param name="LastModifiedUtc">When the settings were last written, or <see langword="null" /> if never.</param>
/// <param name="OriginatingNodeId">The cluster node the last write came from, or <see langword="null" /> if never written.</param>
public record RateLimitingSettingsDetail(
    bool Enable,
    double QueriesPerSecond,
    int BurstSize,
    TimeSpan TrackedClientExpiry,
    TimeSpan CleanupInterval,
    int MaxTrackedClients,
    byte SubnetPrefixLengthIPv4,
    byte SubnetPrefixLengthIPv6,
    TimeSpan SubnetSampleWindow,
    int SubnetUdpQueryLimit,
    int SubnetTcpQueryLimit,
    double SubnetTruncatePercentage,
    IReadOnlyList<string> SubnetBypassCidrs,
    DateTimeOffset? LastModifiedUtc = null,
    Guid? OriginatingNodeId = null
);
