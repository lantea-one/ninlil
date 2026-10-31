using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Control;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Dhcpv4;

/// <summary>
///     This class is the DHCPv4 UDP transport. It binds the server port, receives datagrams from
///     clients and relay agents, decodes them with <see cref="Dhcpv4MessageReader" />, passes each
///     to the <see cref="IDhcpv4RequestHandler" /> (in <c>Ninlil.Dhcp.Server.Core</c>), and sends
///     the reply to the destination RFC 2131 Section 4.1 prescribes. Lease allocation, options
///     and every other server decision belong to the handler.
///     <para>
///         <b>It is a restartable <see cref="IDhcpProtocolService" /></b>, registered as a
///         singleton rather than a hosted service: <see cref="StartAsync" /> and
///         <see cref="StopAsync" /> can be called any number of times, each binding or releasing
///         the socket. <see cref="Dhcpv4ListenerHostedService" /> starts it at boot when
///         <see cref="Dhcpv4ProtocolOptions.Enable" /> is <see langword="true" />.
///         <see cref="_lifecycleLock" /> serializes concurrent start and stop calls, and a call
///         that finds the listener already in the requested state does nothing.
///     </para>
/// </summary>
/// <param name="handler">The handler every decoded request is dispatched to.</param>
/// <param name="options">The listen and reply ports.</param>
/// <param name="logger">The logger this listener records unexpected socket/handler failures to.</param>
public sealed class Dhcpv4UdpListener(
    IDhcpv4RequestHandler handler,
    IOptions<Dhcpv4ProtocolOptions> options,
    ILogger<Dhcpv4UdpListener> logger
) : IDhcpProtocolService
{
    /// <summary>
    ///     This constant contains the receive buffer size: the largest UDP payload over IPv4. A
    ///     DHCPv4 message is far smaller, but a full-size buffer means an oversized datagram is
    ///     read whole rather than truncated by the socket.
    /// </summary>
    private const int MaximumDatagramSize = 65507;

    /// <summary>
    ///     This lock serializes <see cref="StartAsync" /> and <see cref="StopAsync" /> so that
    ///     <see cref="_socket" />, <see cref="_receiveLoopCts" /> and <see cref="IsListening" />
    ///     always change together. It is held only for synchronous field updates, never across an
    ///     <see langword="await" />.
    /// </summary>
    private readonly Lock _lifecycleLock = new();

    /// <summary>
    ///     This field contains the listening socket while the listener runs, or
    ///     <see langword="null" />.
    /// </summary>
    private Socket _socket;

    /// <summary>
    ///     This field contains the cancellation source of the running receive loop, which
    ///     <see cref="StopAsync" /> cancels.
    /// </summary>
    private CancellationTokenSource _receiveLoopCts;

    /// <summary>
    ///     This field contains the running receive loop's task. <see cref="StopAsync" /> awaits
    ///     it so the loop has stopped using the socket before the socket is disposed.
    /// </summary>
    private Task _receiveLoopTask;

    /// <summary>
    ///     This property contains why the last DHCPv4 start failed to bind its socket (the
    ///     operating system's message, for example "Address already in use"), or
    ///     <see langword="null" /> when the last start worked or none has run. The
    ///     <c>dhcpServices</c> health check shows it, because the boot-time start logs the failure
    ///     and goes on instead of stopping the host.
    /// </summary>
    public string StartFailure { get; private set; }

    /// <summary>
    ///     This property reports whether the socket is bound. It stays true if the receive loop
    ///     ends unexpectedly, so <see cref="StopAsync" /> can still release the socket; use
    ///     <see cref="IDhcpProtocolService.IsRunning" /> to learn whether datagrams are being
    ///     served.
    /// </summary>
    public bool IsListening { get; private set; }

    /// <summary>
    ///     This property reports whether the socket is bound and the receive loop is still
    ///     running, so a dead loop is not reported as a healthy listener.
    /// </summary>
    bool IDhcpProtocolService.IsRunning
    {
        get
        {
            lock (_lifecycleLock)
                return IsListening && _receiveLoopTask is { IsCompleted: false };
        }
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        Socket socket;
        CancellationTokenSource receiveLoopCts;

        lock (_lifecycleLock)
        {
            // Starting a running listener does nothing (see IDhcpProtocolService).
            if (IsListening)
            {
                if (_receiveLoopTask is { IsCompleted: false })
                    return Task.CompletedTask;

                // The receive loop ended on its own, so the listener is bound but deaf. Release
                // the old socket and bind again.
                logger.LogWarning(
                    "The DHCPv4 receive loop is not running; releasing the old socket and starting again."
                );

                _socket.Dispose();
                _receiveLoopCts.Dispose();

                _socket = null;
                _receiveLoopCts = null;
                _receiveLoopTask = null;
                IsListening = false;
            }

            // DHCPv4 is IPv4-only. EnableBroadcast is needed to send to the limited-broadcast
            // address; receiving broadcasts needs no option on a wildcard-bound socket.
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            {
                EnableBroadcast = true,
            };

            try
            {
                // SO_REUSEADDR lets this wildcard bind coexist with a socket bound to a specific
                // local address on the same port (that socket gets datagrams for its address,
                // this one the rest), and lets a quick restart re-bind without a conflict.
                socket.SetSocketOption(
                    SocketOptionLevel.Socket,
                    SocketOptionName.ReuseAddress,
                    true
                );
                socket.Bind(new IPEndPoint(IPAddress.Any, options.Value.ServerPort));
            }
            catch (Exception exception)
            {
                // On a bind failure (port in use, for instance) leave IsListening false, keep the
                // reason for the health check and let the caller see the exception.
                socket.Dispose();
                StartFailure = exception.Message;
                throw;
            }

            receiveLoopCts = new CancellationTokenSource();

            _socket = socket;
            _receiveLoopCts = receiveLoopCts;
            IsListening = true;
            StartFailure = null;

            logger.LogInformation(
                "The DHCPv4 listener started and is now bound to UDP port {ServerPort}.",
                options.Value.ServerPort
            );

            _receiveLoopTask = Task.Run(() => RunReceiveLoopAsync(socket, receiveLoopCts.Token));
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Socket socket;
        CancellationTokenSource receiveLoopCts;
        Task receiveLoopTask;

        lock (_lifecycleLock)
        {
            // Stopping a stopped listener does nothing (see IDhcpProtocolService).
            if (!IsListening)
                return;

            socket = _socket;
            receiveLoopCts = _receiveLoopCts;
            receiveLoopTask = _receiveLoopTask;

            _socket = null;
            _receiveLoopCts = null;
            _receiveLoopTask = null;
            IsListening = false;
        }

        try
        {
            // Cancel the token and close the socket. Closing is what unblocks a pending
            // ReceiveFromAsync on platforms where the token alone does not.
            await receiveLoopCts.CancelAsync();
            socket.Close();

            if (receiveLoopTask is not null)
            {
                try
                {
                    await receiveLoopTask.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // Either the caller's token fired while waiting or the loop ended by
                    // cancellation. The socket is already closed, so the loop can no longer use it.
                }
                catch (Exception exception)
                {
                    // A loop that faulted must not skip the disposals below.
                    logger.LogError(exception, "The DHCPv4 receive loop ended with an error.");
                }
            }
        }
        finally
        {
            receiveLoopCts.Dispose();
            socket.Dispose();
        }

        logger.LogInformation("The DHCPv4 listener stopped and released its bound socket.");
    }

    /// <summary>
    ///     This method runs the receive loop for one start/stop cycle, reading datagrams from
    ///     <paramref name="socket" /> until <paramref name="stoppingToken" /> is cancelled and
    ///     handling each with <see cref="ProcessDatagramAsync" /> one at a time. The socket and
    ///     token are passed in rather than read from the fields, because <see cref="StopAsync" />
    ///     clears the fields before the loop notices it should exit. Each datagram is processed
    ///     inside its own try/catch, so one bad datagram never ends the loop. If the loop ends
    ///     while no stop was requested, it logs a Critical entry and
    ///     <see cref="IDhcpProtocolService.IsRunning" /> turns false.
    /// </summary>
    private async Task RunReceiveLoopAsync(Socket socket, CancellationToken stoppingToken)
    {
        try
        {
            await ReceiveDatagramsAsync(socket, stoppingToken);
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogCritical(exception, "The DHCPv4 receive loop stopped unexpectedly.");
            return;
        }

        if (!stoppingToken.IsCancellationRequested)
            logger.LogCritical("The DHCPv4 receive loop stopped unexpectedly.");
    }

    /// <summary>
    ///     This method reads and processes datagrams until a stop is requested or the socket is
    ///     closed. It is the body of <see cref="RunReceiveLoopAsync" />.
    /// </summary>
    private async Task ReceiveDatagramsAsync(Socket socket, CancellationToken stoppingToken)
    {
        byte[] receiveBuffer = new byte[MaximumDatagramSize];

        while (!stoppingToken.IsCancellationRequested)
        {
            SocketReceiveFromResult result;

            try
            {
                result = await socket.ReceiveFromAsync(
                    receiveBuffer,
                    new IPEndPoint(IPAddress.Any, 0),
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
                // On some platforms a cancelled or closed receive surfaces as a SocketException;
                // if a stop was requested, that is what this is.
                if (stoppingToken.IsCancellationRequested)
                    break;

                // Otherwise one failed receive (an ICMP unreachable, for example) must not stop
                // the listener; log it and continue.
                logger.LogWarning(exception, "Error receiving a DHCPv4 datagram.");
                continue;
            }

            byte[] datagram = receiveBuffer[..result.ReceivedBytes];
            IPEndPoint remoteEndPoint = (IPEndPoint)result.RemoteEndPoint;

            try
            {
                await ProcessDatagramAsync(socket, datagram, remoteEndPoint, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // One datagram must never end the loop: that would silence the server for every
                // client. Log it and read the next one.
                logger.LogError(
                    exception,
                    "Processing a DHCPv4 datagram from {RemoteEndPoint} failed.",
                    remoteEndPoint
                );
            }
        }
    }

    /// <summary>
    ///     This method decodes one datagram, calls the request handler and sends any reply. A
    ///     malformed datagram or a handler exception is logged and dropped so that one bad packet
    ///     does not stop the listener. Only a stop request lets an exception escape; the receive
    ///     loop catches anything else.
    /// </summary>
    private async Task ProcessDatagramAsync(
        Socket socket,
        byte[] datagram,
        IPEndPoint remoteEndPoint,
        CancellationToken stoppingToken
    )
    {
        if (
            !Dhcpv4MessageReader.TryRead(
                datagram,
                out Dhcpv4Message request,
                out string failureReason
            )
        )
        {
            // Any host on the network can send junk, so this is not worth a warning.
            logger.LogDebug(
                "Discarding an unparseable DHCPv4 datagram from {RemoteEndPoint}: {Reason}",
                remoteEndPoint,
                failureReason
            );
            return;
        }

        Dhcpv4Message reply;

        try
        {
            reply = await handler.HandleRequestAsync(request, remoteEndPoint, stoppingToken);
        }
        catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
        {
            // This includes a handler that times out with an OperationCanceledException: only a
            // stop request may end the loop.
            logger.LogError(
                exception,
                "The DHCPv4 request handler threw while handling a request from {RemoteEndPoint}.",
                remoteEndPoint
            );
            return;
        }

        if (reply is null)
            return;

        await SendReplyAsync(socket, reply, request, stoppingToken);
    }

    /// <summary>
    ///     This method chooses where a reply goes, as RFC 2131 Section 4.1 says. A request with
    ///     "giaddr" set came through a relay agent, so every reply goes to that agent on the
    ///     server port. With no "giaddr", a DHCPNAK is always broadcast, even when the request set
    ///     "ciaddr" or cleared the broadcast flag, because the client may hold a wrong address
    ///     and not answer ARP. Any other reply is broadcast when the request set the broadcast
    ///     flag, unicast to "ciaddr" when the client has an address (renewing or rebinding), and
    ///     broadcast otherwise.
    ///     <para>
    ///         RFC 2131 Section 4.1 has one more case: with no "giaddr", no "ciaddr" and no
    ///         broadcast flag, the reply should be unicast to "yiaddr" at the client's hardware
    ///         address, without ARP. A managed <see cref="Socket" /> cannot address a send that way
    ///         (it would need a packet socket or an ARP-table entry), so the reply is broadcast
    ///         instead. A client without an address still receives it; the broadcast is only less
    ///         efficient.
    ///     </para>
    /// </summary>
    /// <param name="request">The client request being answered.</param>
    /// <param name="reply">The reply to send; its message type tells a DHCPNAK from the rest.</param>
    /// <param name="serverPort">The DHCP server port a relay agent listens on.</param>
    /// <param name="clientPort">The DHCP client port.</param>
    internal static IPEndPoint ResolveReplyDestination(
        Dhcpv4Message request,
        Dhcpv4Message reply,
        int serverPort,
        int clientPort
    )
    {
        if (!request.RelayAgentIpAddress.Equals(IPAddress.Any))
        {
            // A relay agent forwarded this request; the reply goes back to it on the server port
            // for it to pass on to the client.
            return new IPEndPoint(request.RelayAgentIpAddress, serverPort);
        }

        if (
            reply.TryGetMessageType(out Dhcpv4MessageTypeEnum replyType)
            && replyType == Dhcpv4MessageTypeEnum.Nak
        )
        {
            // RFC 2131 Section 4.1: "when 'giaddr' is zero, the server broadcasts any DHCPNAK".
            return new IPEndPoint(IPAddress.Broadcast, clientPort);
        }

        if (request.BroadcastFlagSet)
            return new IPEndPoint(IPAddress.Broadcast, clientPort);

        if (!request.ClientIpAddress.Equals(IPAddress.Any))
        {
            // The client has an address (renewing or rebinding) and can receive unicast there.
            return new IPEndPoint(request.ClientIpAddress, clientPort);
        }

        // Broadcast instead of the unicast-to-hardware-address case (see the summary).
        return new IPEndPoint(IPAddress.Broadcast, clientPort);
    }

    /// <summary>
    ///     This method encodes the reply and sends it to the destination
    ///     <see cref="ResolveReplyDestination" /> chooses. A reply the writer rejects (an option
    ///     longer than 255 bytes, for example) is logged and dropped.
    /// </summary>
    private async Task SendReplyAsync(
        Socket socket,
        Dhcpv4Message reply,
        Dhcpv4Message request,
        CancellationToken stoppingToken
    )
    {
        byte[] replyBytes;

        try
        {
            replyBytes = Dhcpv4MessageWriter.Write(reply);
        }
        catch (Exception exception)
        {
            reply.TryGetMessageType(out Dhcpv4MessageTypeEnum replyType);

            logger.LogError(
                exception,
                "Could not encode the DHCPv4 {MessageType} reply to a request with transaction ID {TransactionId}; no reply is sent.",
                replyType,
                request.TransactionId
            );
            return;
        }

        IPEndPoint destination = ResolveReplyDestination(
            request,
            reply,
            options.Value.ServerPort,
            options.Value.ClientPort
        );

        try
        {
            await socket.SendToAsync(replyBytes, destination, stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Error sending a DHCPv4 reply to {Destination}.",
                destination
            );
        }
    }
}
