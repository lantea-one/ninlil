// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Refresh;

/// <summary>
///     This class binds to the <c>DnsServer:Authoritative:SecondaryZoneRefresh</c> section of the
///     application's configuration and controls how often <see cref="SecondaryZoneRefreshService" />
///     checks for zones due a refresh, and the retry delay. It is read on each loop iteration.
/// </summary>
public class SecondaryZoneRefreshOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Authoritative:SecondaryZoneRefresh";

    /// <summary>
    ///     This property contains how often the loop checks for due zones. It is not the refresh
    ///     interval, which comes from each zone's SOA Refresh field; a NOTIFY wakes the loop at once
    ///     regardless.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    ///     This property contains how long to wait before retrying a zone that has no local SOA yet
    ///     (so no Retry value), whose primaries were all unreachable, or that is a stub zone.
    /// </summary>
    public TimeSpan DefaultRetryInterval { get; set; } = TimeSpan.FromSeconds(30);
}
