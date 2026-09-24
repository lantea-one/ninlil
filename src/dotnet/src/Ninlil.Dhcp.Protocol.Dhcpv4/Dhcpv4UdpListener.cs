using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Control;
using Ninlil.Dhcp.Common.Wire;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Dhcpv4;

/// <summary>
///     This class implements the DHCPv4 UDP transport itself: binding the configured server
///     port, receiving client (or relay-agent-forwarded) datagrams, decoding them via
///     <see cref="Dhcpv4MessageReader" />, handing the decoded message to whichever
///     <see cref="IDhcpv4RequestHandler" /> dependency injection resolves, and -- per RFC 2131
///     Section 4.1's own destination-selection rules -- sending back whatever reply that handler
///     produces. It does nothing beyond that: lease allocation, option delivery, and every other
///     real server-engine decision belong to the handler, never to this class.
///     <para>
///         <b>This class is a genuinely restartable <see cref="IDhcpProtocolService" /></b>, not a
///         <see cref="Microsoft.Extensions.Hosting.BackgroundService" /> -- <see cref="StartAsync" />/
///         <see cref="StopAsync" /> can be called repeatedly, in either order, for the rest of this
///         process's life, each genuinely binding/releasing a real socket rather than running once
///         for the whole host lifetime. It is registered as an ordinary dependency-injection
///         singleton (never as an <see cref="Microsoft.Extensions.Hosting.IHostedService" />
///         itself) -- <see cref="Dhcpv4ListenerHostedService" /> is the thin wrapper that starts
///         this instance once, automatically, at host boot, only when
///         <see cref="Dhcpv4ProtocolOptions.Enable" /> is <see langword="true" />; see that
///         class's own doc comment, and <see cref="IDhcpProtocolService" />'s, for why configured
///         <c>Enable</c> and this instance's own live running state are two distinct concepts from
///         here on. A concurrent <see cref="StartAsync" />/<see cref="StopAsync" /> pair is
///         serialized through <see cref="_lifecycleLock" />, and each call is a safe, harmless
///         no-op when this service is already in the state being requested.
///     </para>
/// </summary>
/// <param name="handler">The handler every decoded request is dispatched to.</param>
/// <param name="options">This transport's own listen ports.</param>
/// <param name="logger">The logger this listener records unexpected socket/handler failures to.</param>
public sealed class Dhcpv4UdpListener(
    IDhcpv4RequestHandler handler,
    IOptions<Dhcpv4ProtocolOptions> options,
    ILogger<Dhcpv4UdpListener> logger
) : IDhcpProtocolService
{
    /// <summary>
    ///     This constant contains the largest UDP datagram this listener will ever read. A real
    ///     DHCPv4 message, even with a generous options area, runs a small fraction of this; the
    ///     receive buffer is sized to the largest a UDP payload can ever be anyway, so an
    ///     oversized datagram is simply read and discarded downstream rather than truncated by
    ///     the socket call itself.
    /// </summary>
    private const int MaximumDatagramSize = 65507;

    /// <summary>
    ///     This lock serializes every <see cref="StartAsync" />/<see cref="StopAsync" /> call
    ///     against each other, so a concurrent start-while-stopping (or stop-while-starting) pair
    ///     can never leave <see cref="_socket" />/<see cref="_receiveLoopCts" />/
    ///     <see cref="IsListening" /> in an inconsistent combination. Only ever held across plain,
    ///     synchronous field mutation -- never across an <see langword="await" /> -- so it is safe
    ///     to guard both async methods with the same instance.
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

            // DHCPv4 is an IPv4-only protocol (a DHCPv6 listener is its own, separate transport
            // with its own multicast-based semantics), and a server must be able to both receive
            // datagrams sent to the limited-broadcast address and send replies back to it --
            // EnableBroadcast opts this socket into the latter; the operating system delivers
            // incoming broadcasts to a bound socket without any special option needed on the
            // receiving side.
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            {
                EnableBroadcast = true,
            };

            try
            {
                // SO_REUSEADDR lets this wildcard bind coexist with a second, unrelated socket
                // bound to a more specific local address on the same port (the operating system
                // delivers a datagram addressed to that specific address to the more specific
                // socket, and everything else here) -- harmless for this project's own
                // single-instance deployment, and lets a re-bind immediately following a stop (or
                // an operator restarting this process) avoid occasionally hitting a stale,
                // still-lingering bind from the just-released previous socket.
                socket.SetSocketOption(
                    SocketOptionLevel.Socket,
                    SocketOptionName.ReuseAddress,
                    true
                );
                socket.Bind(new IPEndPoint(IPAddress.Any, options.Value.ServerPort));
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
        // a pending ReceiveFromAsync call, since a cancellation token alone does not interrupt a
        // socket operation already in flight on some platforms.
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

        logger.LogInformation("The DHCPv4 listener stopped and released its bound socket.");
    }

    /// <summary>
    ///     This method runs the receive loop for one start/stop cycle: reading datagrams off
    ///     <paramref name="socket" /> until <paramref name="stoppingToken" /> is cancelled,
    ///     dispatching each to <see cref="ProcessDatagramAsync" />. Every socket/cancellation
    ///     value is passed explicitly rather than read back off this instance's own fields, since
    ///     a concurrent <see cref="StopAsync" /> call clears those fields as soon as it claims the
    ///     stop, strictly before this loop has necessarily noticed and exited.
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
                // Cancelling a pending receive (via StopAsync closing the socket, or the token
                // itself) can surface here as a SocketException rather than
                // OperationCanceledException/ObjectDisposedException on some platforms; if we
                // were asked to stop, this is that, not a datagram-level failure to log and shrug
                // off.
                if (stoppingToken.IsCancellationRequested)
                    break;

                // Otherwise, a malformed or unreachable-sender-triggered failure on one datagram
                // must never take down the whole listener; log it and keep serving everyone else.
                logger.LogWarning(exception, "Error receiving a DHCPv4 datagram.");
                continue;
            }

            byte[] datagram = receiveBuffer[..result.ReceivedBytes];
            IPEndPoint remoteEndPoint = (IPEndPoint)result.RemoteEndPoint;

            await ProcessDatagramAsync(socket, datagram, remoteEndPoint, stoppingToken);
        }
    }

    /// <summary>
    ///     This method decodes one received datagram, invokes the request handler, and sends
    ///     back whatever reply it produces. A malformed datagram or a handler failure is logged
    ///     and otherwise ignored -- one bad or hostile packet must never take down the whole
    ///     listener.
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
    ///     This method sends an encoded reply to whichever destination RFC 2131 Section 4.1 says
    ///     it belongs at, based on the original request's own "giaddr"/"ciaddr" fields and
    ///     broadcast flag.
    ///     <para>
    ///         RFC 2131 Section 4.1 describes a fourth case this method does not implement
    ///         precisely: when a request carries no relay-agent address, no client address, and
    ///         no broadcast flag, the RFC calls for the reply to be unicast to the client's own
    ///         offered address (<c>yiaddr</c>) using the client's hardware address directly,
    ///         bypassing ARP resolution entirely, since that address is not yet configured or
    ///         reachable by any ordinary route. A managed <see cref="Socket" /> has no portable
    ///         way to address a send that way; doing so for real needs either a raw/packet socket
    ///         or a platform-specific mechanism to inject an ARP-table entry, neither of which
    ///         exists in this listener. A well-behaved client with no address configured yet --
    ///         the only situation this fourth case can arise in -- always sets the broadcast flag
    ///         for exactly this reason, so this method falls back to broadcasting the reply
    ///         instead, which reaches such a client correctly; it is a real, deliberate, and
    ///         currently unaddressed limitation only for a client that violates that expectation.
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
            // A relay agent forwarded this request; the reply always goes back to it, at the
            // server port, for the relay agent to forward on to the real client in turn.
            destination = new IPEndPoint(request.RelayAgentIpAddress, options.Value.ServerPort);
        }
        else if (request.BroadcastFlagSet)
        {
            destination = new IPEndPoint(IPAddress.Broadcast, options.Value.ClientPort);
        }
        else if (!request.ClientIpAddress.Equals(IPAddress.Any))
        {
            // The client already has a usable address (renewing/rebinding) and can receive an
            // ordinary unicast reply there.
            destination = new IPEndPoint(request.ClientIpAddress, options.Value.ClientPort);
        }
        else
        {
            // See this method's own doc comment for why this falls back to broadcasting.
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
