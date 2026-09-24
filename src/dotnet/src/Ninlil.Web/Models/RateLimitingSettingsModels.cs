// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST representation of this host's own currently-effective
///     <c>DnsRateLimitOptions</c>. No secret material is involved, so this record's own fields are
///     the exact same ones a <c>PATCH</c> to the same endpoint accepts.
/// </summary>
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
