// Define our namespace.
namespace Ninlil.Dns.Server.Core.Pipeline;

/// <summary>
///     This delegate sends an encoded response back to whichever client sent the query it
///     answers. Each transport supplies its own implementation of this alongside every
///     <see cref="DnsInboundMessage" /> it enqueues -- writing to a UDP socket at a remembered
///     endpoint, or completing an in-flight HTTP response, or writing to an open QUIC stream --
///     so that <see cref="DnsQueryProcessingPipeline" /> can send a reply without needing to know
///     anything about which transport the query arrived over.
/// </summary>
/// <param name="responseBytes">The encoded response to send.</param>
/// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
public delegate ValueTask DnsResponseSenderAsync(
    ReadOnlyMemory<byte> responseBytes,
    CancellationToken stoppingToken
);
