using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding;

/// <summary>
///     This class binds to the <c>DnsServer:Forwarding</c> configuration section and controls
///     <see cref="ForwarderQueryResolver" />, <see cref="ForwarderHealthTracker" /> and
///     <see cref="Caching.ForwardedResponseCache" />. All three read it live, so
///     <c>ForwardingSettingsController</c> changes apply without a restart. The forwarders
///     themselves are stored as <c>DnsForwarderEntity</c> rows and forwarder zones, not here.
/// </summary>
public class ForwardingOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Forwarding";

    /// <summary>
    ///     This property controls whether this server forwards at all. It is off by default, so an
    ///     operator opts in. Forwarding is recursive service, so it also needs the client to set
    ///     RD and to be allowed recursion: <see cref="Recursive.RecursiveServerOptions.EnableRecursion" /> on,
    ///     or an access rule granting the client recursion access (see
    ///     <see cref="Recursive.Resolution.RecursionPermission" />). A forward-only network that keeps
    ///     <see cref="Recursive.RecursiveServerOptions.EnableRecursion" /> off therefore adds one such rule
    ///     for its clients.
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    ///     This property controls how <see cref="ForwarderQueryResolver" /> picks among multiple
    ///     configured forwarders. The default, <see cref="ForwarderFailoverStrategyEnum.HealthCheckedRoundRobin" />,
    ///     routes around a failing forwarder instead of trying it first on every query.
    /// </summary>
    public ForwarderFailoverStrategyEnum FailoverStrategy { get; set; } =
        ForwarderFailoverStrategyEnum.HealthCheckedRoundRobin;

    /// <summary>
    ///     This property contains how often <see cref="ForwarderHealthTracker" /> probes every
    ///     forwarder it knows about, when <see cref="FailoverStrategy" /> is
    ///     <see cref="ForwarderFailoverStrategyEnum.HealthCheckedRoundRobin" />.
    /// </summary>
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     This property contains how long a single forwarder may take to answer a single query
    ///     (or a single health check) before it is treated as failed for that attempt.
    /// </summary>
    public TimeSpan PerForwarderTimeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    ///     This property controls whether forwarded answers are cached
    ///     (<see cref="Caching.ForwardedResponseCache" />), so a repeat of the same question is
    ///     answered locally until the answer's TTL runs out. It is on by default, since without it
    ///     every lookup pays an upstream round trip. Turning it off stops lookups and new stores
    ///     at once; what is already cached stays until it expires or is flushed.
    /// </summary>
    public bool EnableCache { get; set; } = true;

    /// <summary>
    ///     This property contains how many forwarded answers may be cached before the
    ///     soonest-to-expire are evicted, enforced the same way as the recursive cache's
    ///     <c>MaxCacheEntries</c>. Each entry is a whole response, so the default is lower than
    ///     the recursive cache's per-RRset bound; ten thousand covers a household's working set of
    ///     names.
    /// </summary>
    public int MaxCacheEntries { get; set; } = 10000;
}
