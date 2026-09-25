// Define our namespace.
namespace Ninlil.Dns.Server.Core.Pipeline;

/// <summary>
///     This delegate sends an encoded response to the client that sent the query. Each
///     transport supplies one with every <see cref="DnsInboundMessage" /> it enqueues (sending
///     a UDP datagram to the remembered endpoint, completing an HTTP response, writing to a QUIC
///     stream), so <see cref="DnsQueryProcessingPipeline" /> can reply without knowing the
///     transport. The pipeline calls it at most once; a dropped query never calls it.
/// </summary>
/// <param name="responseBytes">The encoded response to send.</param>
/// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
public delegate ValueTask DnsResponseSenderAsync(
    ReadOnlyMemory<byte> responseBytes,
    CancellationToken stoppingToken
);
