using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Forwarding;

/// <summary>
///     This interface is implemented once per <see cref="UpstreamForwarderProtocolEnum" /> value
///     (plain UDP/TCP, DNS-over-TLS, DNS-over-HTTPS, DNS-over-QUIC, DNSCrypt) in
///     <c>Ninlil.Dns.Server.Forwarding</c>. Each sends a query to a
///     <see cref="UpstreamForwarderDefinition" /> of its <see cref="Protocol" />. All are
///     registered together, and the forwarder resolver picks one by protocol.
/// </summary>
public interface IUpstreamForwarderClient
{
    /// <summary>
    ///     This property contains which protocol this client speaks.
    /// </summary>
    UpstreamForwarderProtocolEnum Protocol { get; }

    /// <summary>
    ///     This method forwards a single query to an upstream forwarder and returns its response.
    /// </summary>
    /// <param name="definition">
    ///     The forwarder to query. Its <see cref="UpstreamForwarderDefinition.Protocol" /> must
    ///     match this client's <see cref="Protocol" />.
    /// </param>
    /// <param name="query">The query to forward, as received from the client (possibly with ECS added).</param>
    /// <param name="stoppingToken">
    ///     The token used to observe cancellation. A caller that wants a per-forwarder timeout
    ///     links it to a timeout token; there is no timeout parameter.
    /// </param>
    /// <returns>The forwarder's response.</returns>
    /// <exception cref="OperationCanceledException">
    ///     Thrown when <paramref name="stoppingToken" /> is cancelled before a response arrives,
    ///     including by a caller's timeout. Network and parse errors surface as other exceptions;
    ///     failover callers treat any exception as "this forwarder did not answer".
    /// </exception>
    Task<DnsMessage> ForwardAsync(
        UpstreamForwarderDefinition definition,
        DnsMessage query,
        CancellationToken stoppingToken = default
    );
}
