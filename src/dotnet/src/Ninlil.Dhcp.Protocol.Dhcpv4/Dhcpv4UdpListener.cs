using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Control;
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
    ///     This property reports whether the socket is bound. It is not cleared if the receive
    ///     loop ends on an unexpected exception.
    /// </summary>
    public bool IsListening { get; private set; }

    /// <inheritdoc />
    bool IDhcpProtocolService.IsRunning => IsListening;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        Socket socket;
        CancellationTokenSource receiveLoopCts;

        lock (_lifecycleLock)
        {
            // Starting a running listener does nothing (see IDhcpProtocolService).
            if (IsListening)
                return Task.CompletedTask;

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
            catch
            {
                // On a bind failure (port in use, for instance) leave IsListening false and let
                // the caller see the exception.
                socket.Dispose();
                throw;
            }

            receiveLoopCts = new CancellationTokenSource();

            _socket = socket;
            _receiveLoopCts = receiveLoopCts;
            IsListening = true;

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
                // Either the caller's token fired while waiting or the loop ended by cancellation.
                // The socket is already closed, so the loop can no longer use it.
            }
        }

        receiveLoopCts.Dispose();
        socket.Dispose();

        logger.LogInformation("The DHCPv4 listener stopped and released its bound socket.");
    }

    /// <summary>
    ///     This method runs the receive loop for one start/stop cycle, reading datagrams from
    ///     <paramref name="socket" /> until <paramref name="stoppingToken" /> is cancelled and
    ///     handling each with <see cref="ProcessDatagramAsync" /> one at a time. The socket and
    ///     token are passed in rather than read from the fields, because <see cref="StopAsync" />
    ///     clears the fields before the loop notices it should exit. An exception other than the
    ///     receive errors caught here ends the loop.
    /// </summary>
    private async Task RunReceiveLoopAsync(Socket socket, CancellationToken stoppingToken)
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

            await ProcessDatagramAsync(socket, datagram, remoteEndPoint, stoppingToken);
        }
    }

    /// <summary>
    ///     This method decodes one datagram, calls the request handler and sends any reply. A
    ///     malformed datagram or a handler exception is logged and dropped so that one bad packet
    ///     does not stop the listener. An exception from encoding the reply is not caught here
    ///     (see <see cref="SendReplyAsync" />).
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
            logger.LogWarning(
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
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
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
    ///     This method encodes the reply and sends it where RFC 2131 Section 4.1 says, based on the
    ///     request's "giaddr" and "ciaddr" fields and broadcast flag: to the relay agent, by
    ///     broadcast, or by unicast to the client's address. Encoding happens outside the
    ///     try/catch, so a reply the writer rejects throws out of the receive loop. A DHCPNAK
    ///     follows the same rules, so it is unicast to a client that set "ciaddr".
    ///     <para>
    ///         RFC 2131 Section 4.1 has one more case: with no "giaddr", no "ciaddr" and no
    ///         broadcast flag, the reply should be unicast to "yiaddr" at the client's hardware
    ///         address, without ARP. A managed <see cref="Socket" /> cannot address a send that way
    ///         (it would need a packet socket or an ARP-table entry), so the reply is broadcast
    ///         instead. A client without an address still receives it; the broadcast is only less
    ///         efficient.
    ///     </para>
    /// </summary>
    private async Task SendReplyAsync(
        Socket socket,
        Dhcpv4Message reply,
        Dhcpv4Message request,
        CancellationToken stoppingToken
    )
    {
        byte[] replyBytes = Dhcpv4MessageWriter.Write(reply);

        IPEndPoint destination;

        if (!request.RelayAgentIpAddress.Equals(IPAddress.Any))
        {
            // A relay agent forwarded this request; the reply goes back to it on the server port
            // for it to pass on to the client.
            destination = new IPEndPoint(request.RelayAgentIpAddress, options.Value.ServerPort);
        }
        else if (request.BroadcastFlagSet)
        {
            destination = new IPEndPoint(IPAddress.Broadcast, options.Value.ClientPort);
        }
        else if (!request.ClientIpAddress.Equals(IPAddress.Any))
        {
            // The client has an address (renewing or rebinding) and can receive unicast there.
            destination = new IPEndPoint(request.ClientIpAddress, options.Value.ClientPort);
        }
        else
        {
            // Broadcast instead of the unicast-to-hardware-address case (see the summary).
            destination = new IPEndPoint(IPAddress.Broadcast, options.Value.ClientPort);
        }

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
