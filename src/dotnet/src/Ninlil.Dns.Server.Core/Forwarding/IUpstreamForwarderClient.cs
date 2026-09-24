using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Forwarding;

/// <summary>
///     This interface is implemented once per <see cref="UpstreamForwarderProtocolEnum" /> value
///     -- a plain UDP/TCP client, a DNS-over-TLS client, a DNS-over-HTTPS client, a DNS-over-QUIC
///     client, and a DNSCrypt client -- each knowing how to forward a single query against a
///     <see cref="UpstreamForwarderDefinition" /> matching its own <see cref="Protocol" />.
///     Registered as <see cref="IEnumerable{T}" /> in dependency injection, the same
///     multi-strategy shape <c>ICloudDnsProvider</c> already establishes for its own cloud
///     connectors.
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
    ///     The forwarder to query -- its own <see cref="UpstreamForwarderDefinition.Protocol" />
    ///     must equal this client's own <see cref="Protocol" />.
    /// </param>
    /// <param name="query">The query to forward, exactly as received from the original client.</param>
    /// <param name="stoppingToken">
    ///     The token used to observe cancellation of the operation -- a caller enforcing a
    ///     per-forwarder timeout links this to a timeout-bound token rather than this method
    ///     accepting a timeout parameter directly.
    /// </param>
    /// <returns>The forwarder's own response.</returns>
    /// <exception cref="OperationCanceledException">
    ///     Thrown when <paramref name="stoppingToken" /> is cancelled before a response arrives
    ///     -- including a caller-enforced timeout -- so a health-checking/failover caller can
    ///     treat every failure mode (network error, malformed response, timeout) uniformly as
    ///     "this forwarder did not answer," without needing to distinguish exception types.
    /// </exception>
    Task<DnsMessage> ForwardAsync(
        UpstreamForwarderDefinition definition,
        DnsMessage query,
        CancellationToken stoppingToken = default
    );
}
