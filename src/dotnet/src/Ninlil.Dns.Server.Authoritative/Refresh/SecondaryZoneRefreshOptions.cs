// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Refresh;

/// <summary>
///     This class binds to the <c>DnsServer:Authoritative:SecondaryZoneRefresh</c> section of the
///     application's configuration and controls how often <see cref="SecondaryZoneRefreshService" />
///     checks whether any secondary zone is due for a refresh.
/// </summary>
public class SecondaryZoneRefreshOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Authoritative:SecondaryZoneRefresh";

    /// <summary>
    ///     This property contains how often the background loop wakes up to check whether any
    ///     secondary zone's own refresh timer (or an immediate request from a NOTIFY) has come
    ///     due. This is polling granularity, not the refresh interval itself -- an individual
    ///     zone's own SOA Refresh field (or, for an urgent NOTIFY-triggered check, "immediately")
    ///     still governs when it is actually due; this just bounds how promptly "immediately" can
    ///     really happen.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    ///     This property contains how long to wait before retrying a zone that has no local SOA
    ///     record yet to read its own Retry interval from, or whose primaries were all
    ///     unreachable on the last attempt.
    /// </summary>
    public TimeSpan DefaultRetryInterval { get; set; } = TimeSpan.FromSeconds(30);
}
