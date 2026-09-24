using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding;

/// <summary>
///     This class binds to the <c>DnsServer:Forwarding</c> section of the application's
///     configuration and controls <see cref="ForwarderQueryResolver" />/<see cref="ForwarderHealthTracker" />.
/// </summary>
public class ForwardingOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Forwarding";

    /// <summary>
    ///     This property controls whether this server forwards at all. Off by default -- the
    ///     same "off by default for capability-expanding toggles" convention every other
    ///     transport/toggle in this project already follows.
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    ///     This property controls how <see cref="ForwarderQueryResolver" /> picks among multiple
    ///     configured forwarders. Defaults to <see cref="ForwarderFailoverStrategyEnum.HealthCheckedRoundRobin" />,
    ///     so a failing forwarder is automatically routed around rather than silently degrading
    ///     every query until an operator notices.
    /// </summary>
    public ForwarderFailoverStrategyEnum FailoverStrategy { get; set; } =
        ForwarderFailoverStrategyEnum.HealthCheckedRoundRobin;

    /// <summary>
    ///     This property contains how often <see cref="ForwarderHealthTracker" /> re-checks every
    ///     configured forwarder's own health, when <see cref="FailoverStrategy" /> is
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
    ///     answered locally until the answer's own TTL runs out instead of going upstream again.
    ///     On by default: every resolver a home network would otherwise use caches, and without
    ///     it every single lookup pays a full upstream round trip. Turning it off stops both
    ///     lookups and new stores immediately; what is already cached stays until it expires or
    ///     is flushed.
    /// </summary>
    public bool EnableCache { get; set; } = true;

    /// <summary>
    ///     This property contains how many forwarded answers may be cached before the
    ///     soonest-to-expire are evicted to make room -- the forwarded counterpart of the
    ///     recursive resolver's own <c>MaxCacheEntries</c>, bounded and enforced the same way.
    ///     Each entry is one whole response, so the default is lower than the recursive cache's
    ///     per-RRset bound; ten thousand is the size dnsmasq-based home resolvers ship with and
    ///     comfortably covers a household's working set of names.
    /// </summary>
    public int MaxCacheEntries { get; set; } = 10000;
}
