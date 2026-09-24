// Define our namespace.
namespace Ninlil.Dns.Server.Core.Transport;

/// <summary>
///     This class binds to the <c>DnsServer:OutboundQuery</c> section of the application's
///     configuration and controls <see cref="OutboundUdpSocketPool" />'s own behavior: the
///     source-port randomization/pooling every outbound UDP query this server itself issues (as
///     a recursive resolver toward authoritative servers, and as a forwarder toward configured
///     upstream resolvers) uses, per this sub-phase's own anti-cache-poisoning hardening goal.
/// </summary>
public sealed class OutboundQueryOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:OutboundQuery";

    /// <summary>
    ///     This property controls whether outbound UDP queries are sent through
    ///     <see cref="OutboundUdpSocketPool" /> at all. On by default: unlike a capability that
    ///     exposes new information or opens a new listening surface, a pooled, excluded-port-aware
    ///     source port is a pure hardening improvement over this project's previous behavior (a
    ///     brand-new, fully OS-ephemeral-random socket per query) with no new attack surface of its
    ///     own, matching the same "self-maintaining capabilities default on" bias every other
    ///     pure-correctness/hardening toggle in this project already follows. When
    ///     <see langword="false" />, <c>Ninlil.Dns.Server.Recursive.Transport.UpstreamDnsClient</c>
    ///     and <c>Ninlil.Dns.Server.Forwarding.Clients.PlainForwarderClient</c> fall back to their
    ///     original behavior: a fresh, one-off socket per query, bound to whatever port the OS
    ///     assigns. Plain <c>&lt;c&gt;</c> text, not <c>&lt;see cref&gt;</c>, since this project
    ///     does not (and, per its own layering, must not) reference either of those two consuming
    ///     projects at compile time.
    /// </summary>
    public bool EnableSourcePortPool { get; set; } = true;

    /// <summary>
    ///     This property contains how many outbound UDP sockets this pool keeps ready per address
    ///     family (IPv4 and IPv6 are tracked, and therefore capped, independently) -- effectively
    ///     how many distinct source ports this server's own outbound queries rotate across at
    ///     once. A query issued while every pooled socket for its own family is already checked
    ///     out waits for one to be returned rather than opening an unbounded number of concurrent
    ///     sockets, the same backpressure-over-unbounded-growth posture
    ///     <see cref="RateLimiting.IDnsRateLimiter" />'s own <c>MaxTrackedClients</c> cap takes for
    ///     a different resource.
    /// </summary>
    public int PoolSizePerAddressFamily { get; set; } = 128;

    /// <summary>
    ///     This property contains local source ports this pool never binds an outbound socket to
    ///     -- for example, a port some other local service on the same host already listens on,
    ///     or one an operator otherwise wants this server's own outbound DNS traffic to avoid.
    ///     Empty by default (no port excluded).
    /// </summary>
    public List<int> ExcludedSourcePorts { get; set; } = [];
}
