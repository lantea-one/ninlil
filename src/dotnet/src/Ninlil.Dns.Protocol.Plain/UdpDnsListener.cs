using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.Pipeline;

// Define our namespace.
namespace Ninlil.Dns.Protocol.Plain;

/// <summary>
///     This class implements RFC 1035's original DNS transport: one query per UDP datagram, one
///     response per datagram back. It does nothing beyond receiving bytes and handing them to
///     <see cref="DnsQueryProcessingPipeline" /> -- decoding, resolving, encoding, and RFC 1035
///     §4.2.1 truncation are all the shared pipeline's own responsibility, not duplicated here.
/// </summary>
/// <param name="pipeline">The pipeline every received datagram is enqueued onto.</param>
/// <param name="options">This transport's own listen address and port.</param>
/// <param name="logger">The logger this listener records unexpected socket failures to.</param>
public sealed class UdpDnsListener(
    DnsQueryProcessingPipeline pipeline,
    IOptions<PlainProtocolOptions> options,
    ILogger<UdpDnsListener> logger
) : BackgroundService
{
    /// <summary>
    ///     This constant contains the largest UDP datagram this listener will ever read. 65527
    ///     octets is the theoretical maximum a UDP payload can occupy; nothing this server sends
    ///     or expects to receive legitimately approaches it, but the receive buffer is sized to
    ///     it anyway so an oversized datagram is simply read and discarded downstream rather than
    ///     truncated by the socket call itself.
    /// </summary>
    private const int MaximumDatagramSize = 65527;

    /// <summary>
    ///     This property contains the listening socket, once <see cref="ExecuteAsync" /> has
    ///     bound one -- kept here so <see cref="StopAsync" /> can close it, which is what
    ///     actually unblocks the receive loop's own <see cref="Socket.ReceiveFromAsync(Memory{byte},SocketFlags,EndPoint,CancellationToken)" />
    ///     call during shutdown.
    /// </summary>
    private Socket _socket;

    /// <summary>
    ///     This property reports whether this listener's own socket is currently bound and
    ///     accepting datagrams -- read by the <c>/status</c> health-check endpoint, which needs a
    ///     real "am I bound and running" signal from every transport rather than just the fact
    ///     that it was registered/configured.
    /// </summary>
    public bool IsListening { get; private set; }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // A dual-stack socket, bound to the IPv6 wildcard address with IPv6Only disabled, accepts
        // both IPv6 and (via the OS's own IPv4-mapped-address translation) IPv4 clients through
        // one socket, rather than needing two separate listeners for the two address families.
        _socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
        _socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
        _socket.Bind(new IPEndPoint(IPAddress.IPv6Any, options.Value.Port));
        IsListening = true;

        byte[] receiveBuffer = new byte[MaximumDatagramSize];

        while (!stoppingToken.IsCancellationRequested)
        {
            SocketReceiveFromResult result;

            try
            {
                result = await _socket.ReceiveFromAsync(
                    receiveBuffer,
                    new IPEndPoint(IPAddress.IPv6Any, 0),
                    stoppingToken
                );
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                // The socket was closed by StopAsync while a receive was still pending.
                break;
            }
            catch (SocketException exception)
            {
                // Cancelling a pending receive (via StopAsync closing the socket, or the token
                // itself) can surface here as a SocketException rather than
                // OperationCanceledException/ObjectDisposedException, the same observed
                // platform/runtime behavior TcpDnsListener's own accept loop guards against; if
                // we were asked to stop, this is that, not a datagram-level failure to log and
                // shrug off.
                if (stoppingToken.IsCancellationRequested)
                    break;

                // Otherwise, a malformed or unreachable-sender-triggered ICMP error on one
                // datagram must never take down the whole listener; log it and keep serving
                // everyone else.
                logger.LogWarning(exception, "Error receiving a UDP datagram.");
                continue;
            }

            byte[] datagram = receiveBuffer[..result.ReceivedBytes];
            IPEndPoint senderEndpoint = (IPEndPoint)result.RemoteEndPoint;

            await pipeline.EnqueueAsync(
                new DnsInboundMessage(
                    datagram,
                    DnsTransportProtocolEnum.Udp,
                    senderEndpoint.Address,
                    (responseBytes, sendToken) =>
                        SendResponseAsync(responseBytes, senderEndpoint, sendToken)
                ),
                stoppingToken
            );
        }
    }

    /// <summary>
    ///     This method sends a single encoded response back to whichever endpoint its query
    ///     arrived from.
    /// </summary>
    private async ValueTask SendResponseAsync(
        ReadOnlyMemory<byte> responseBytes,
        IPEndPoint senderEndpoint,
        CancellationToken stoppingToken
    )
    {
        try
        {
            await _socket.SendToAsync(responseBytes, senderEndpoint, stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The client may simply be gone (a firewall reset, a since-closed ephemeral port);
            // there is no one left to report this failure to.
            logger.LogWarning(
                exception,
                "Error sending a UDP response to {Endpoint}.",
                senderEndpoint
            );
        }
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        IsListening = false;
        _socket?.Close();
        await base.StopAsync(cancellationToken);
    }
}
