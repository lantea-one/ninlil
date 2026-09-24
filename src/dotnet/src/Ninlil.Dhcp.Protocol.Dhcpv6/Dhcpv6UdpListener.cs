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
///     This class implements the DHCPv6 UDP transport itself: binding the configured server
///     port, joining the RFC 8415 Section 7.1 multicast group every DHCPv6 client sends to,
///     receiving client datagrams, decoding them via <see cref="Dhcpv6MessageReader" />, handing
///     the decoded message to whichever <see cref="IDhcpv6RequestHandler" /> dependency injection
///     resolves, and sending back whatever reply that handler produces -- the DHCPv6 analogue of
///     <c>Ninlil.Dhcp.Protocol.Dhcpv4.Dhcpv4UdpListener</c>. It does nothing beyond that: address
///     assignment, Prefix Delegation, and every other real server-engine decision belong to the
///     handler, never to this class.
///     <para>
///         <b>RELAY-FORW/RELAY-REPL messages</b> arriving on this same socket are recognized by
///         their own leading "msg-type" byte before <see cref="Dhcpv6MessageReader" /> ever sees
///         them (that ordinary reader rejects them outright -- see its own doc comment), decoded
///         instead via <see cref="Dhcpv6RelayMessageReader" />, and dispatched to whichever
///         <see cref="IDhcpv6RelayMessageHandler" /> dependency injection resolves -- resolved as
///         an <b>optional</b> service (see <see cref="IDhcpv6RelayMessageHandler" />'s own doc
///         comment for why), since a deployment with no relay-adjacent role active at all has
///         nothing registered for it and simply discards a relay message it happens to receive.
///     </para>
///     <para>
///         <b>This class is a genuinely restartable <see cref="IDhcpProtocolService" /></b>, not a
///         <see cref="Microsoft.Extensions.Hosting.BackgroundService" /> -- <see cref="StartAsync" />/
///         <see cref="StopAsync" /> can be called repeatedly, in either order, for the rest of
///         this process's life, each genuinely binding/releasing a real socket (and joining/
///         leaving its multicast group membership) rather than running once for the whole host
///         lifetime. It is registered as an ordinary dependency-injection singleton (never as an
///         <see cref="Microsoft.Extensions.Hosting.IHostedService" /> itself) --
///         <see cref="Dhcpv6ListenerHostedService" /> is the thin wrapper that starts this
///         instance once, automatically, at host boot, only when
///         <see cref="Dhcpv6ProtocolOptions.Enable" /> is <see langword="true" />; see that
///         class's own doc comment, and <see cref="IDhcpProtocolService" />'s, for why configured
///         <c>Enable</c> and this instance's own live running state are two distinct concepts
///         from here on. A concurrent <see cref="StartAsync" />/<see cref="StopAsync" /> pair is
///         serialized through <see cref="_lifecycleLock" />, and each call is a safe, harmless
///         no-op when this service is already in the state being requested.
///     </para>
/// </summary>
/// <param name="handler">The handler every decoded ordinary request is dispatched to.</param>
/// <param name="options">This transport's own listen ports.</param>
/// <param name="logger">The logger this listener records unexpected socket/handler failures to.</param>
/// <param name="serviceProvider">
///     Used to resolve <see cref="IDhcpv6RelayMessageHandler" /> as an optional dependency (see
///     this class's own doc comment) -- left <see langword="null" /> by a caller that never
///     expects to receive a relay message at all, such as a test constructing this listener
///     directly rather than through dependency injection.
/// </param>
public sealed class Dhcpv6UdpListener(
    IDhcpv6RequestHandler handler,
    IOptions<Dhcpv6ProtocolOptions> options,
    ILogger<Dhcpv6UdpListener> logger,
    IServiceProvider serviceProvider = null
) : IDhcpProtocolService
{
    /// <summary>
    ///     This constant contains the largest UDP datagram this listener will ever read -- the
    ///     same reasoning <c>Dhcpv4UdpListener.MaximumDatagramSize</c>'s own doc comment gives.
    /// </summary>
    private const int MaximumDatagramSize = 65507;

    /// <summary>
    ///     This lock serializes every <see cref="StartAsync" />/<see cref="StopAsync" /> call
    ///     against each other -- the same reasoning
    ///     <c>Dhcpv4UdpListener._lifecycleLock</c>'s own doc comment gives.
    /// </summary>
    private readonly Lock _lifecycleLock = new();

    /// <summary>
    ///     This field contains the listening socket, once <see cref="StartAsync" /> has bound
    ///     one, cleared again by <see cref="StopAsync" /> once it has been released.
    /// </summary>
    private Socket _socket;

    /// <summary>
    ///     This field contains the cancellation source that owns the currently running receive
    ///     loop's own lifetime, cancelled by <see cref="StopAsync" /> to unblock its pending
    ///     receive call and signal it to exit.
    /// </summary>
    private CancellationTokenSource _receiveLoopCts;

    /// <summary>
    ///     This field contains the currently running receive loop's own task, awaited by
    ///     <see cref="StopAsync" /> to confirm the loop has genuinely exited (and, with it, that
    ///     no further use of <see cref="_socket" /> is still in flight) before that socket is
    ///     disposed.
    /// </summary>
    private Task _receiveLoopTask;

    /// <summary>
    ///     This field contains the optional relay-message handler, re-resolved at the top of
    ///     every <see cref="StartAsync" /> call rather than once for the whole process -- see
    ///     this class's own doc comment for why this is an optional dependency rather than a
    ///     required constructor parameter.
    /// </summary>
    private IDhcpv6RelayMessageHandler _relayMessageHandler;

    /// <summary>
    ///     This property reports whether this listener's own socket is currently bound and
    ///     accepting datagrams.
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
            // Calling Start while already running is a safe, harmless no-op -- see this class's
            // own doc comment and IDhcpProtocolService's.
            if (IsListening)
                return Task.CompletedTask;

            // Resolved fresh on every start, rather than cached once for the whole process -- see
            // this class's own doc comment for why this is an optional dependency, resolved by
            // hand rather than as an ordinary required constructor parameter.
            _relayMessageHandler = serviceProvider?.GetService<IDhcpv6RelayMessageHandler>();

            socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);

            try
            {
                // The same coexistence/restart-friendliness reasoning
                // Dhcpv4UdpListener.StartAsync's own doc comment gives for SO_REUSEADDR applies
                // here too.
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
                // A genuine bind failure (the port already in use, for instance) must leave this
                // service reporting IsListening = false, not half-started -- propagate the
                // exception to the caller rather than swallowing it.
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
            // Calling Stop while already stopped is a safe, harmless no-op -- see this class's
            // own doc comment and IDhcpProtocolService's.
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

        // Cancel the loop's own token and close the socket -- closing is what actually unblocks
        // a pending ReceiveFromAsync call, and also drops this socket's own multicast-group
        // membership, since that membership is scoped to the socket's own lifetime.
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
                // Either this call's own cancellationToken fired while waiting, or the loop task
                // itself completed via cancellation -- either way, the loop has stopped touching
                // the socket by the time this method returns, which is this method's own real
                // contract.
            }
        }

        receiveLoopCts.Dispose();
        socket.Dispose();

        logger.LogInformation("The DHCPv6 listener stopped and released its bound socket.");
    }

    /// <summary>
    ///     This method joins <see cref="Dhcpv6MulticastGroups.AllDhcpRelayAgentsAndServers" /> on
    ///     every network interface that is currently up and IPv6-capable, since a DHCPv6 server
    ///     must be reachable by a client on any link it serves, not merely whichever single
    ///     interface the operating system would otherwise pick as a socket's own "default"
    ///     multicast interface. Failing to join on one specific interface (a virtual/tunnel
    ///     adapter that rejects multicast membership, for example) is logged and otherwise
    ///     ignored -- it must never prevent this listener from serving every other, healthy
    ///     interface.
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
    ///     This method runs the receive loop for one start/stop cycle -- the DHCPv6 analogue of
    ///     <c>Dhcpv4UdpListener.RunReceiveLoopAsync</c>, with the same reasoning for why every
    ///     socket/cancellation value is passed explicitly rather than read back off this
    ///     instance's own fields.
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
    ///     This method decodes one received datagram, invokes the request handler, and sends
    ///     back whatever reply it produces -- the same shape
    ///     <c>Dhcpv4UdpListener.ProcessDatagramAsync</c> establishes. A datagram whose own leading
    ///     "msg-type" byte names RELAY-FORW/RELAY-REPL is diverted to
    ///     <see cref="ProcessRelayDatagramAsync" /> instead, before <see cref="Dhcpv6MessageReader" />
    ///     -- which rejects that shape outright -- ever sees it.
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
    ///     This method decodes one received RELAY-FORW/RELAY-REPL datagram, invokes whichever
    ///     <see cref="IDhcpv6RelayMessageHandler" /> is registered (if any), and sends back
    ///     whatever reply it produces, directly to <paramref name="remoteEndPoint" /> -- never
    ///     through <see cref="SendReplyAsync" />, which assumes an ordinary client/server message.
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
    ///     This method sends an encoded reply directly back, via unicast, to the address the
    ///     original request was received from.
    ///     <para>
    ///         Unlike DHCPv4 (see <c>Dhcpv4UdpListener.SendReplyAsync</c>'s own doc comment for
    ///         its real RFC 2131 Section 4.1 destination-selection logic), a DHCPv6 client always
    ///         has a usable source address to be answered at -- its own link-local address -- even
    ///         before it has any address this server assigned, since IPv6 requires an interface to
    ///         configure a link-local address on its own before doing anything else on the
    ///         network. A client reaches a server by multicasting to
    ///         <see cref="Dhcpv6MulticastGroups.AllDhcpRelayAgentsAndServers" />, but the request's
    ///         own source address is always that real, reachable unicast address, never the
    ///         multicast destination it was sent to -- so no DHCPv4-style broadcast fallback, and
    ///         no relay-agent-address branching, is needed here at all. A genuine relay agent
    ///         forwarding a client's message via RELAY-FORW is a different message shape entirely
    ///         (see <see cref="Common.Enum.Dhcpv6MessageTypeEnum.RelayForward" />'s own doc
    ///         comment), decoded and answered by <see cref="ProcessRelayDatagramAsync" /> instead
    ///         -- this method is only ever called for an ordinary, unrelayed client/server message.
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
