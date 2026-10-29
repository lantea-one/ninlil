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
///     This class implements the RFC 1035 DNS transport over UDP: one query per datagram, one
///     response per datagram. It only receives bytes and hands them to
///     <see cref="DnsQueryProcessingPipeline" />, which decodes, resolves, encodes and applies
///     RFC 1035 §4.2.1 truncation (see that class). The client address it passes on is the
///     sender's address as the socket reports it; on the default dual-stack socket an IPv4
///     client appears as an IPv4-mapped IPv6 address, which <see cref="DnsInboundMessage" />
///     turns into plain IPv4. The raw endpoint is kept to send the reply.
/// </summary>
/// <param name="pipeline">The pipeline every received datagram is enqueued onto.</param>
/// <param name="options">
///     The listen addresses and port, read through <see cref="IOptionsMonitor{TOptions}" /> each
///     time <see cref="ExecuteAsync" /> starts, so restarting this listener (the
///     <c>plain-udp</c> managed service) picks up a changed port or address list without
///     restarting the process.
/// </param>
/// <param name="logger">The logger this listener records bind problems and unexpected socket failures to.</param>
public sealed class UdpDnsListener(
    DnsQueryProcessingPipeline pipeline,
    IOptionsMonitor<PlainProtocolOptions> options,
    ILogger<UdpDnsListener> logger
) : BackgroundService
{
    /// <summary>
    ///     This constant contains the size of the receive buffer: 65535 minus the 8-octet UDP
    ///     header, at least as large as any UDP payload. No legitimate query comes close, but a
    ///     buffer this size means an oversized datagram is read whole and rejected by the
    ///     pipeline rather than truncated by the socket call.
    /// </summary>
    private const int MaximumDatagramSize = 65527;

    /// <summary>
    ///     This field contains the bound sockets, one per listen address (or a single dual-stack
    ///     one when none are configured). <see cref="StopAsync" /> closes them, which unblocks each
    ///     receive loop's pending
    ///     <see cref="Socket.ReceiveFromAsync(Memory{byte},SocketFlags,SocketAddress,CancellationToken)" />
    ///     call.
    /// </summary>
    private ListenSocketSet _sockets = ListenSocketSet.Empty;

    /// <summary>
    ///     This property reports whether at least one of this listener's sockets is bound and
    ///     receiving. <c>TransportListenersHealthCheck</c> reads it, since being registered or
    ///     configured does not mean a transport is running.
    /// </summary>
    public bool IsListening { get; private set; }

    /// <summary>
    ///     This property lists every configured listen address this listener is not serving, each
    ///     as "address (reason)". The health check reads it so a partly bound listener shows as
    ///     degraded rather than healthy.
    /// </summary>
    public IReadOnlyList<string> FailedListenAddresses => _sockets.FailedAddresses;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        PlainProtocolOptions current = options.CurrentValue;

        // With no listen addresses configured this is one dual-stack socket, bound to the IPv6
        // wildcard with IPv6Only off, so it accepts IPv6 clients and (as IPv4-mapped addresses)
        // IPv4 clients. Otherwise it is one socket per address; see ListenSocketSet for how a
        // failing address is handled.
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
    ///     <para>
    ///         The loop receives into a buffer and a sender <see cref="SocketAddress" /> that it
    ///         owns and the next receive overwrites. Everything handed on is therefore built
    ///         before the next receive: a new array with the datagram bytes (the pipeline keeps a
    ///         reference to <see cref="DnsInboundMessage.RequestBytes" /> for the life of the
    ///         query, so a slice of the receive buffer would be overwritten under it) and a new
    ///         <see cref="IPEndPoint" /> for the reply. Each loop must keep its own buffer and
    ///         address for the same reason.
    ///     </para>
    ///     <para>
    ///         The reply goes out through the <see cref="IPEndPoint" /> overload of
    ///         <see cref="Socket.SendToAsync(ReadOnlyMemory{byte},EndPoint,CancellationToken)" />.
    ///         The <see cref="SocketAddress" /> overload throws an
    ///         <see cref="ArgumentException" /> ("Null is not a valid value for
    ///         e.RemoteEndPoint") now and then when many replies are sent at once, which would
    ///         lose the reply.
    ///     </para>
    /// </summary>
    private async Task ReceiveLoopAsync(Socket socket, CancellationToken stoppingToken)
    {
        byte[] receiveBuffer = new byte[MaximumDatagramSize];
        SocketAddress senderAddress = new(socket.AddressFamily);

        // The template that turns a SocketAddress into an IPEndPoint has to match the socket's
        // family.
        IPEndPoint addressTemplate = new(
            socket.AddressFamily == AddressFamily.InterNetworkV6
                ? IPAddress.IPv6Any
                : IPAddress.Any,
            0
        );

        while (!stoppingToken.IsCancellationRequested)
        {
            int receivedBytes;

            try
            {
                // The sender's address length is reset before each receive, as the socket
                // writes it back on completion.
                senderAddress.Size = senderAddress.Buffer.Length;
                receivedBytes = await socket.ReceiveFromAsync(
                    receiveBuffer,
                    SocketFlags.None,
                    senderAddress,
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
                // Cancelling a pending receive (StopAsync closing the socket, or the token) can
                // surface as a SocketException rather than OperationCanceledException or
                // ObjectDisposedException, as in TcpDnsListener's accept loop. If we were asked
                // to stop, that is what this is.
                if (stoppingToken.IsCancellationRequested)
                    break;

                // Otherwise it is an error on one datagram (for example an ICMP error from an
                // unreachable sender), which must not stop the listener; log it and keep going.
                logger.LogWarning(exception, "Error receiving a UDP datagram.");
                continue;
            }

            // A fresh array and a fresh endpoint per datagram: see the summary above.
            byte[] datagram = receiveBuffer.AsSpan(0, receivedBytes).ToArray();
            IPEndPoint senderEndpoint = (IPEndPoint)addressTemplate.Create(senderAddress);

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
            // The client may be gone (a firewall reset, a closed ephemeral port); there is no one
            // to report the failure to.
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
