using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Control;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Dhcpv6;

/// <summary>
///     This class is the DHCPv6 UDP transport, the counterpart of
///     <c>Ninlil.Dhcp.Protocol.Dhcpv4.Dhcpv4UdpListener</c>. It binds the server port, joins the
///     RFC 8415 Section 7.1 All_DHCP_Relay_Agents_and_Servers group on every interface, decodes
///     client datagrams with <see cref="Dhcpv6MessageReader" />, passes them to the
///     <see cref="IDhcpv6RequestHandler" /> and unicasts the reply back. Address assignment,
///     prefix delegation and every other server decision belong to the handler.
///     <para>
///         <b>Relay messages.</b> A datagram whose first byte is RELAY-FORW or RELAY-REPL is
///         decoded with <see cref="Dhcpv6RelayMessageReader" /> instead and passed to the
///         <see cref="IDhcpv6RelayMessageHandler" />, an optional service: when none is registered
///         the message is discarded.
///     </para>
///     <para>
///         <b>It is a restartable <see cref="IDhcpProtocolService" /></b>, registered as a
///         singleton, with the same lifecycle as the DHCPv4 listener: <see cref="StartAsync" /> and
///         <see cref="StopAsync" /> bind and release the socket (and its multicast memberships)
///         any number of times, <see cref="Dhcpv6ListenerHostedService" /> starts it at boot when
///         <see cref="Dhcpv6ProtocolOptions.Enable" /> is <see langword="true" />, and
///         <see cref="_lifecycleLock" /> serializes concurrent calls.
///     </para>
/// </summary>
/// <param name="handler">The handler client/server requests are passed to.</param>
/// <param name="options">The listen and reply ports.</param>
/// <param name="logger">The logger this listener records unexpected socket/handler failures to.</param>
/// <param name="serviceProvider">
///     Used to resolve the optional <see cref="IDhcpv6RelayMessageHandler" />. Tests that build
///     the listener directly may leave it <see langword="null" />, and relay messages are then
///     discarded.
/// </param>
public sealed class Dhcpv6UdpListener(
    IDhcpv6RequestHandler handler,
    IOptions<Dhcpv6ProtocolOptions> options,
    ILogger<Dhcpv6UdpListener> logger,
    IServiceProvider serviceProvider = null
) : IDhcpProtocolService
{
    /// <summary>
    ///     This constant contains the receive buffer size, chosen as in
    ///     <c>Dhcpv4UdpListener.MaximumDatagramSize</c>.
    /// </summary>
    private const int MaximumDatagramSize = 65507;

    /// <summary>
    ///     This lock serializes <see cref="StartAsync" /> and <see cref="StopAsync" />, as in
    ///     <c>Dhcpv4UdpListener._lifecycleLock</c>.
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
    ///     This field contains the optional relay-message handler, resolved again on every
    ///     <see cref="StartAsync" />, or <see langword="null" /> when none is registered.
    /// </summary>
    private IDhcpv6RelayMessageHandler _relayMessageHandler;

    /// <summary>
    ///     This property reports whether the socket is bound. As in the DHCPv4 listener, it is
    ///     not cleared if the receive loop ends on an unexpected exception.
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

            // The relay handler is optional, so it is looked up rather than injected.
            _relayMessageHandler = serviceProvider?.GetService<IDhcpv6RelayMessageHandler>();

            socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);

            try
            {
                // SO_REUSEADDR for the same reasons as in Dhcpv4UdpListener.StartAsync.
                socket.SetSocketOption(
                    SocketOptionLevel.Socket,
                    SocketOptionName.ReuseAddress,
                    true
                );
                socket.Bind(new IPEndPoint(IPAddress.IPv6Any, options.Value.ServerPort));
                JoinMulticastGroupOnEveryUsableInterface(socket);
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
                "The DHCPv6 listener started and is now bound to UDP port {ServerPort}.",
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

        // Cancel the token and close the socket. Closing unblocks a pending ReceiveFromAsync and
        // drops the multicast memberships, which belong to the socket.
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

        logger.LogInformation("The DHCPv6 listener stopped and released its bound socket.");
    }

    /// <summary>
    ///     This method joins <see cref="Dhcpv6MulticastGroups.AllDhcpRelayAgentsAndServers" /> on
    ///     every interface that is up and supports IPv6, so clients on any attached link can reach
    ///     the server, not only those on the operating system's default multicast interface. A
    ///     failure on one interface (a tunnel that rejects multicast, for example) is logged and
    ///     skipped. Interfaces that come up after the listener starts are not joined until the
    ///     next start.
    /// </summary>
    private void JoinMulticastGroupOnEveryUsableInterface(Socket socket)
    {
        foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (
                networkInterface.OperationalStatus != OperationalStatus.Up
                || !networkInterface.Supports(NetworkInterfaceComponent.IPv6)
            )
                continue;

            try
            {
                int interfaceIndex = networkInterface.GetIPProperties().GetIPv6Properties().Index;

                socket.SetSocketOption(
                    SocketOptionLevel.IPv6,
                    SocketOptionName.AddMembership,
                    new IPv6MulticastOption(
                        Dhcpv6MulticastGroups.AllDhcpRelayAgentsAndServers,
                        interfaceIndex
                    )
                );
            }
            catch (Exception exception)
                when (exception is SocketException or NetworkInformationException)
            {
                logger.LogWarning(
                    exception,
                    "Could not join the DHCPv6 multicast group on interface {InterfaceName}.",
                    networkInterface.Name
                );
            }
        }
    }

    /// <summary>
    ///     This method runs the receive loop for one start/stop cycle, as
    ///     <c>Dhcpv4UdpListener.RunReceiveLoopAsync</c> does, with the socket and token passed in
    ///     for the same reason.
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
                if (stoppingToken.IsCancellationRequested)
                    break;

                logger.LogWarning(exception, "Error receiving a DHCPv6 datagram.");
                continue;
            }

            byte[] datagram = receiveBuffer[..result.ReceivedBytes];
            IPEndPoint remoteEndPoint = (IPEndPoint)result.RemoteEndPoint;

            await ProcessDatagramAsync(socket, datagram, remoteEndPoint, stoppingToken);
        }
    }

    /// <summary>
    ///     This method decodes one datagram, calls the request handler and sends any reply, as
    ///     <c>Dhcpv4UdpListener.ProcessDatagramAsync</c> does. A RELAY-FORW or RELAY-REPL goes to
    ///     <see cref="ProcessRelayDatagramAsync" /> instead, since
    ///     <see cref="Dhcpv6MessageReader" /> rejects relay messages.
    /// </summary>
    private async Task ProcessDatagramAsync(
        Socket socket,
        byte[] datagram,
        IPEndPoint remoteEndPoint,
        CancellationToken stoppingToken
    )
    {
        if (datagram.Length > 0 && IsRelayMessageType(datagram[0]))
        {
            await ProcessRelayDatagramAsync(socket, datagram, remoteEndPoint, stoppingToken);
            return;
        }

        if (
            !Dhcpv6MessageReader.TryRead(
                datagram,
                out Dhcpv6Message request,
                out string failureReason
            )
        )
        {
            logger.LogWarning(
                "Discarding an unparseable DHCPv6 datagram from {RemoteEndPoint}: {Reason}",
                remoteEndPoint,
                failureReason
            );
            return;
        }

        Dhcpv6Message reply;

        try
        {
            reply = await handler.HandleRequestAsync(request, remoteEndPoint, stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "The DHCPv6 request handler threw while handling a request from {RemoteEndPoint}.",
                remoteEndPoint
            );
            return;
        }

        if (reply is null)
            return;

        await SendReplyAsync(socket, reply, remoteEndPoint, stoppingToken);
    }

    /// <summary>
    ///     This method reports whether a raw "msg-type" byte names RELAY-FORW or RELAY-REPL.
    /// </summary>
    private static bool IsRelayMessageType(byte messageTypeByte) =>
        messageTypeByte == (byte)Dhcpv6MessageTypeEnum.RelayForward
        || messageTypeByte == (byte)Dhcpv6MessageTypeEnum.RelayReply;

    /// <summary>
    ///     This method decodes one RELAY-FORW or RELAY-REPL, calls the registered
    ///     <see cref="IDhcpv6RelayMessageHandler" /> (if any) and sends any reply straight back to
    ///     <paramref name="remoteEndPoint" />, the relay agent's address and port. Unlike
    ///     <see cref="SendReplyAsync" />, an encoding failure is caught and logged.
    /// </summary>
    private async Task ProcessRelayDatagramAsync(
        Socket socket,
        byte[] datagram,
        IPEndPoint remoteEndPoint,
        CancellationToken stoppingToken
    )
    {
        if (_relayMessageHandler is null)
        {
            logger.LogDebug(
                "Discarding a DHCPv6 relay message (RELAY-FORW/RELAY-REPL) from {RemoteEndPoint}: no {InterfaceName} is registered.",
                remoteEndPoint,
                nameof(IDhcpv6RelayMessageHandler)
            );
            return;
        }

        if (
            !Dhcpv6RelayMessageReader.TryRead(
                datagram,
                out Dhcpv6RelayMessage request,
                out string failureReason
            )
        )
        {
            logger.LogWarning(
                "Discarding an unparseable DHCPv6 relay datagram from {RemoteEndPoint}: {Reason}",
                remoteEndPoint,
                failureReason
            );
            return;
        }

        Dhcpv6RelayMessage reply;

        try
        {
            reply = await _relayMessageHandler.HandleRelayMessageAsync(
                request,
                remoteEndPoint,
                stoppingToken
            );
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "The DHCPv6 relay-message handler threw while handling a message from {RemoteEndPoint}.",
                remoteEndPoint
            );
            return;
        }

        if (reply is null)
            return;

        byte[] replyBytes;

        try
        {
            replyBytes = Dhcpv6RelayMessageWriter.Write(reply);
        }
        catch (ArgumentException exception)
        {
            logger.LogError(
                exception,
                "The DHCPv6 relay-message handler produced a reply this codec could not encode."
            );
            return;
        }

        try
        {
            await socket.SendToAsync(replyBytes, remoteEndPoint, stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Error sending a DHCPv6 relay reply to {Destination}.",
                remoteEndPoint
            );
        }
    }

    /// <summary>
    ///     This method encodes the reply and unicasts it to the request's source address on the
    ///     client port. As in the DHCPv4 listener, encoding happens outside the try/catch.
    ///     <para>
    ///         Unlike DHCPv4 (see <c>Dhcpv4UdpListener.SendReplyAsync</c>), no destination choice
    ///         is needed: a DHCPv6 client always has a link-local address, which IPv6 configures
    ///         before anything else, and its request comes from that unicast address even though
    ///         it was sent to the multicast group. Relayed messages arrive as RELAY-FORW and are
    ///         answered by <see cref="ProcessRelayDatagramAsync" />, so this method only sees
    ///         unrelayed client messages.
    ///     </para>
    /// </summary>
    private async Task SendReplyAsync(
        Socket socket,
        Dhcpv6Message reply,
        IPEndPoint remoteEndPoint,
        CancellationToken stoppingToken
    )
    {
        byte[] replyBytes = Dhcpv6MessageWriter.Write(reply);
        IPEndPoint destination = new(remoteEndPoint.Address, options.Value.ClientPort);

        try
        {
            await socket.SendToAsync(replyBytes, destination, stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Error sending a DHCPv6 reply to {Destination}.",
                destination
            );
        }
    }
}
