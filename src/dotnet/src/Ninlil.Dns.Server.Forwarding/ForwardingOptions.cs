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
}
