using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.Transport;

// Define our namespace.
namespace Ninlil.Dns.Protocol.Plain;

/// <summary>
///     This class implements RFC 1035's original DNS transport: one query per UDP datagram, one
///     response per datagram back. It does nothing beyond receiving bytes and handing them to
///     <see cref="DnsQueryProcessingPipeline" /> -- decoding, resolving, encoding, and RFC 1035
///     §4.2.1 truncation are all the shared pipeline's own responsibility, not duplicated here.
/// </summary>
/// <param name="pipeline">The pipeline every received datagram is enqueued onto.</param>
/// <param name="options">
///     This transport's listen addresses and port -- read through
///     <see cref="IOptionsMonitor{TOptions}" /> each time <see cref="ExecuteAsync" /> starts, so
///     restarting just this listener (<c>POST /api/services/plain-udp/restart</c>) picks up a
///     changed port or address list without restarting the whole process.
/// </param>
/// <param name="logger">The logger this listener records bind problems and unexpected socket failures to.</param>
public sealed class UdpDnsListener(
    DnsQueryProcessingPipeline pipeline,
    IOptionsMonitor<PlainProtocolOptions> options,
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
    ///     This property contains the bound sockets, one per listen address (or a single
    ///     dual-stack one when none are configured) -- kept here so <see cref="StopAsync" /> can
    ///     close them, which is what actually unblocks each receive loop's pending
    ///     <see cref="Socket.ReceiveFromAsync(Memory{byte},SocketFlags,EndPoint,CancellationToken)" />
    ///     call during shutdown.
    /// </summary>
    private ListenSocketSet _sockets = ListenSocketSet.Empty;

    /// <summary>
    ///     This property reports whether at least one of this listener's sockets is bound and
    ///     accepting datagrams -- read by the <c>/status</c> health-check endpoint, which needs a
    ///     real "am I bound and running" signal from every transport rather than just the fact
    ///     that it was registered/configured.
    /// </summary>
    public bool IsListening { get; private set; }

    /// <summary>
    ///     This property lists every configured listen address this listener is not serving, each
    ///     as "address (reason)" -- read by the <c>/status</c> health check so a partly-bound
    ///     listener shows up as degraded instead of looking healthy.
    /// </summary>
    public IReadOnlyList<string> FailedListenAddresses => _sockets.FailedAddresses;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        PlainProtocolOptions current = options.CurrentValue;

        // With no listen addresses configured this is one dual-stack socket -- bound to the IPv6
        // wildcard with IPv6Only off, so it accepts IPv6 and (via the OS's IPv4-mapped-address
        // translation) IPv4 clients alike. Otherwise it's one socket per address; see
        // ListenSocketSet for how a failing address is handled.
        _sockets = ListenSocketSet.Bind(
            "Plain DNS (UDP)",
            current.ListenAddresses,
            current.Port,
            ProtocolType.Udp,
            logger
        );

        if (_sockets.Sockets.Count == 0)
            return;

        IsListening = true;

        await Task.WhenAll(
            _sockets.Sockets.Select(socket => ReceiveLoopAsync(socket, stoppingToken))
        );
    }

    /// <summary>
    ///     This method receives datagrams on one socket until shutdown. Each socket gets its own
    ///     loop, and every response goes back out through the socket its query arrived on, so the
    ///     client sees the reply come from the address it asked.
    /// </summary>
    private async Task ReceiveLoopAsync(Socket socket, CancellationToken stoppingToken)
    {
        byte[] receiveBuffer = new byte[MaximumDatagramSize];

        // The "any sender" placeholder ReceiveFromAsync needs has to match the socket's family.
        EndPoint anySender = new IPEndPoint(
            socket.AddressFamily == AddressFamily.InterNetworkV6
                ? IPAddress.IPv6Any
                : IPAddress.Any,
            0
        );

        while (!stoppingToken.IsCancellationRequested)
        {
            SocketReceiveFromResult result;

            try
            {
                result = await socket.ReceiveFromAsync(receiveBuffer, anySender, stoppingToken);
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
                        SendResponseAsync(socket, responseBytes, senderEndpoint, sendToken)
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
        Socket socket,
        ReadOnlyMemory<byte> responseBytes,
        IPEndPoint senderEndpoint,
        CancellationToken stoppingToken
    )
    {
        try
        {
            await socket.SendToAsync(responseBytes, senderEndpoint, stoppingToken);
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
        _sockets.Close();
        await base.StopAsync(cancellationToken);
    }
}
