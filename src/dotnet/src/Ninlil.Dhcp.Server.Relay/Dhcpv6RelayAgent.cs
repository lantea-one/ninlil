using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Protocol.Dhcpv6;
using Ninlil.Dhcp.Server.Relay.Identity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Relay;

/// <summary>
///     This class is the real RFC 8415 DHCPv6 relay agent: the alternative
///     <see cref="IDhcpv6RequestHandler" />/<see cref="IDhcpv6RelayMessageHandler" />
///     implementation <see cref="Dhcpv6UdpListener" /> dispatches every decoded datagram to when
///     this server is configured to relay rather than answer locally (see
///     <see cref="Dhcpv6RelayOptions" />'s own doc comment for why the two are mutually
///     exclusive) -- the DHCPv6 analogue of <see cref="Dhcpv4RelayAgent" />.
///     <para>
///         <b>Why this needs its own background receive loop, unlike <see cref="Dhcpv4RelayAgent" /></b>:
///         DHCPv4's own relay agent needs no receive loop of its own at all, because RFC 2131
///         Section 4.1 has a server unicast its reply to "the 'DHCP server' port on the BOOTP
///         relay agent whose address appears in 'giaddr'" -- a fixed, well-known port, regardless
///         of which port the original forwarded request's own source happened to be. That lets
///         <see cref="Dhcpv4RelayAgent" />'s reply arrive back through the very same well-known
///         port <c>Dhcpv4UdpListener</c> already listens on. DHCPv6 has no equivalent convention:
///         RFC 8415's RELAY-FORW/RELAY-REPL exchange is an ordinary, symmetric UDP request/reply --
///         a server replies to whatever source address and port the RELAY-FORW actually arrived
///         from. Since this relay's own dedicated outbound socket (<see cref="_outboundSocket" />)
///         sends every RELAY-FORW from its own ephemeral local port, the upstream server's own
///         RELAY-REPL naturally targets that exact same ephemeral port -- never the shared,
///         well-known port <see cref="Dhcpv6UdpListener" /> binds -- so this class runs its own
///         small receive loop directly on that same dedicated socket (registered as a
///         <see cref="BackgroundService" />) rather than depending on
///         <see cref="Dhcpv6UdpListener" />'s own separate listening socket for this leg at all.
///         <see cref="Dhcpv6UdpListener" />'s own generic RELAY-FORW/RELAY-REPL dispatch mechanism
///         (see its own doc comment) exists for the genuinely different "receiving side" role
///         instead -- an external relay agent forwarding a client's message <i>to</i> this server,
///         handled by <c>Ninlil.Dhcp.Server.Core.Dhcpv6RequestHandler</c>'s own
///         <see cref="IDhcpv6RelayMessageHandler" /> implementation, never by this class.
///     </para>
///     <para>
///         Like <see cref="Dhcpv4RelayAgent" />, this class's own <see cref="HandleRequestAsync" />
///         (an ordinary client message arriving via <see cref="Dhcpv6UdpListener" />'s own shared,
///         multicast-joined socket) always returns <see langword="null" /> -- every real send this
///         class ever makes goes out through its own dedicated outbound socket instead, whether
///         forwarding a client's message upstream or relaying a reply back down.
///     </para>
/// </summary>
public sealed class Dhcpv6RelayAgent
    : BackgroundService,
        IDhcpv6RequestHandler,
        IDhcpv6RelayMessageHandler
{
    /// <summary>
    ///     This constant contains the largest UDP datagram this relay's own receive loop will ever
    ///     read -- the same reasoning <c>Dhcpv6UdpListener.MaximumDatagramSize</c>'s own doc
    ///     comment gives.
    /// </summary>
    private const int MaximumDatagramSize = 65507;

    /// <summary>
    ///     This constant bounds how many times a RELAY-REPL this relay receives back may report
    ///     having already been relayed (RFC 8415 Section 7.1's own "hop-count" field) before this
    ///     relay refuses to process it -- the same defensive, RFC-independent loop-prevention
    ///     reasoning <c>Dhcpv4RelayAgent.MaxHops</c>'s own doc comment already documents. This
    ///     relay itself always sends a RELAY-FORW with <c>hop-count</c> <c>0</c> (this project's
    ///     own relay support forwards exactly one hop -- see
    ///     <see cref="Dhcpv6RelayMessage" />'s own doc comment), so a genuine upstream server's own
    ///     RELAY-REPL always echoes that same <c>0</c> back per RFC 8415 Section 16; a materially
    ///     higher value reaching this check would mean something other than this relay's own
    ///     genuine upstream reply arrived on this socket.
    /// </summary>
    private const byte MaxHops = 16;

    private readonly IOptions<Dhcpv6RelayOptions> _relayOptions;
    private readonly IOptions<Dhcpv6ProtocolOptions> _protocolOptions;
    private readonly IDhcpv6RelayAgentAddressProvider _addressProvider;
    private readonly ILogger<Dhcpv6RelayAgent> _logger;

    /// <summary>
    ///     This property contains the socket every outbound send (both the upstream-bound
    ///     RELAY-FORW and the client-bound relayed reply) goes out through, and the socket
    ///     <see cref="ExecuteAsync" /> also receives a genuine upstream RELAY-REPL back on -- see
    ///     this class's own doc comment for why the same socket handles both directions here,
    ///     unlike <see cref="Dhcpv4RelayAgent" />.
    /// </summary>
    private readonly Socket _outboundSocket;

    /// <param name="relayOptions">This relay's own upstream-server and Interface-ID configuration.</param>
    /// <param name="protocolOptions">The transport's own configured client port, used for a relayed reply's own destination.</param>
    /// <param name="addressProvider">This relay's own resolved link-address.</param>
    /// <param name="logger">The logger this relay's own forwarding decisions are recorded to.</param>
    public Dhcpv6RelayAgent(
        IOptions<Dhcpv6RelayOptions> relayOptions,
        IOptions<Dhcpv6ProtocolOptions> protocolOptions,
        IDhcpv6RelayAgentAddressProvider addressProvider,
        ILogger<Dhcpv6RelayAgent> logger
    )
    {
        _relayOptions = relayOptions;
        _protocolOptions = protocolOptions;
        _addressProvider = addressProvider;
        _logger = logger;

        _outboundSocket = new Socket(
            AddressFamily.InterNetworkV6,
            SocketType.Dgram,
            ProtocolType.Udp
        );
        _outboundSocket.Bind(new IPEndPoint(IPAddress.IPv6Any, 0));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_relayOptions.Value.Enable)
        {
            // Registration is already conditional on this same flag (see
            // Extension.ServiceCollectionDhcpv6RelayExtension.AddNinlilDhcpv6Relay and Program.cs's
            // own composition-root branch), so this should never actually be reached -- kept as
            // defense in depth, mirroring the same "checks its own Enable flag" posture every
            // other optional transport/engine in this project already establishes.
            return;
        }

        byte[] receiveBuffer = new byte[MaximumDatagramSize];

        while (!stoppingToken.IsCancellationRequested)
        {
            SocketReceiveFromResult result;

            try
            {
                result = await _outboundSocket.ReceiveFromAsync(
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
                // The socket was closed by Dispose while a receive was still pending.
                break;
            }
            catch (SocketException exception)
            {
                if (stoppingToken.IsCancellationRequested)
                    break;

                _logger.LogWarning(
                    exception,
                    "Error receiving a datagram on this relay's own outbound socket."
                );
                continue;
            }

            byte[] datagram = receiveBuffer[..result.ReceivedBytes];
            IPEndPoint remoteEndPoint = (IPEndPoint)result.RemoteEndPoint;

            if (
                !Dhcpv6RelayMessageReader.TryRead(
                    datagram,
                    out Dhcpv6RelayMessage message,
                    out string failureReason
                )
            )
            {
                _logger.LogWarning(
                    "Discarding an unparseable datagram received on this relay's own outbound socket from {RemoteEndPoint}: {Reason}",
                    remoteEndPoint,
                    failureReason
                );
                continue;
            }

            try
            {
                // The result is always null here (see HandleRelayMessageAsync's own doc comment)
                // -- this loop exists purely to drive the receive-and-process side of that method,
                // not to send anything further of its own.
                await HandleRelayMessageAsync(message, remoteEndPoint, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(
                    exception,
                    "Error processing a DHCPv6 relay message received from {RemoteEndPoint}.",
                    remoteEndPoint
                );
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask<Dhcpv6Message> HandleRequestAsync(
        Dhcpv6Message request,
        IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken
    )
    {
        await RelayRequestUpstreamAsync(request, remoteEndPoint, cancellationToken);

        // This class always sends every real datagram itself, through its own outbound socket
        // (see this class's own doc comment for why) -- there is never anything left for
        // Dhcpv6UdpListener to send back to remoteEndPoint on this class's behalf.
        return null;
    }

    /// <summary>
    ///     This method implements the forwarding half of this relay's own job: a client's message,
    ///     received on <see cref="Dhcpv6UdpListener" />'s own shared, multicast-joined socket, is
    ///     wrapped in a RELAY-FORW envelope and unicast to every configured upstream DHCPv6 server.
    /// </summary>
    private async Task RelayRequestUpstreamAsync(
        Dhcpv6Message request,
        IPEndPoint clientEndPoint,
        CancellationToken cancellationToken
    )
    {
        List<string> upstreamAddresses = _relayOptions.Value.UpstreamServerAddresses;

        if (upstreamAddresses is not { Count: > 0 })
        {
            _logger.LogWarning(
                "Dropping a DHCPv6 message this relay would otherwise forward: no upstream server addresses are configured ({OptionName}).",
                $"{Dhcpv6RelayOptions.SectionName}:{nameof(Dhcpv6RelayOptions.UpstreamServerAddresses)}"
            );
            return;
        }

        Dhcpv6RelayMessage relayForward = new()
        {
            MessageType = Dhcpv6MessageTypeEnum.RelayForward,
            // This project's own relay support forwards exactly one hop -- see
            // Dhcpv6RelayMessage's own doc comment -- so this relay is always "the" relay agent
            // for a message it forwards, never a later hop in some longer chain.
            HopCount = 0,
            LinkAddress = _addressProvider.Value,
            PeerAddress = clientEndPoint.Address,
        };
        relayForward.SetRelayMessagePayload(Dhcpv6MessageWriter.Write(request));

        if (!string.IsNullOrEmpty(_relayOptions.Value.InterfaceId))
            relayForward.SetInterfaceId(Encoding.ASCII.GetBytes(_relayOptions.Value.InterfaceId));

        byte[] wireBytes;

        try
        {
            wireBytes = Dhcpv6RelayMessageWriter.Write(relayForward);
        }
        catch (ArgumentException exception)
        {
            _logger.LogError(exception, "Could not encode a RELAY-FORW envelope to forward.");
            return;
        }

        int upstreamPort = _relayOptions.Value.UpstreamServerPort;

        // A real relay agent conventionally forwards to every configured upstream server at once
        // (see Dhcpv6RelayOptions.UpstreamServerAddresses's own doc comment) -- whichever one
        // replies first wins.
        foreach (string upstreamAddressText in upstreamAddresses)
        {
            if (
                !IPAddress.TryParse(upstreamAddressText, out IPAddress upstreamAddress)
                || upstreamAddress.AddressFamily != AddressFamily.InterNetworkV6
            )
            {
                _logger.LogWarning(
                    "Skipping an invalid configured upstream DHCPv6 server address: '{Address}'.",
                    upstreamAddressText
                );
                continue;
            }

            try
            {
                await _outboundSocket.SendToAsync(
                    wireBytes,
                    new IPEndPoint(upstreamAddress, upstreamPort),
                    cancellationToken
                );
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(
                    exception,
                    "Error forwarding a DHCPv6 message to upstream server {Address}.",
                    upstreamAddress
                );
            }
        }
    }

    /// <inheritdoc />
    public async Task<Dhcpv6RelayMessage> HandleRelayMessageAsync(
        Dhcpv6RelayMessage message,
        IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken
    )
    {
        // Only ever process a genuine RELAY-REPL here -- a RELAY-FORW reaching this relay's own
        // outbound socket would mean something else entirely was sent to it, never a real upstream
        // server's own reply to a message this relay itself forwarded.
        if (message.MessageType != Dhcpv6MessageTypeEnum.RelayReply)
        {
            _logger.LogDebug(
                "Ignoring a {MessageType} received on this relay's own outbound socket from {RemoteEndPoint}; only a RELAY-REPL is ever expected here.",
                message.MessageType,
                remoteEndPoint
            );
            return null;
        }

        if (message.HopCount > MaxHops)
        {
            _logger.LogWarning(
                "Discarding a RELAY-REPL from {RemoteEndPoint} whose own hop-count ({HopCount}) exceeds this relay's own defensive limit ({MaxHops}).",
                remoteEndPoint,
                message.HopCount,
                MaxHops
            );
            return null;
        }

        if (!message.TryGetRelayMessagePayload(out byte[] innerPayload))
        {
            _logger.LogWarning(
                "Discarding a RELAY-REPL from {RemoteEndPoint} carrying no Relay Message option.",
                remoteEndPoint
            );
            return null;
        }

        if (
            !Dhcpv6MessageReader.TryRead(
                innerPayload,
                out Dhcpv6Message innerReply,
                out string failureReason
            )
        )
        {
            _logger.LogWarning(
                "Discarding a RELAY-REPL from {RemoteEndPoint} whose own nested message could not be decoded: {Reason}",
                remoteEndPoint,
                failureReason
            );
            return null;
        }

        // message.PeerAddress is the original client's own address, exactly as this relay itself
        // set it when building the RELAY-FORW this reply answers -- RFC 8415 Section 16 has a
        // relay agent extract and deliver the inner message to that same peer, unicast, once it
        // strips the relay envelope back off.
        int clientPort = _protocolOptions.Value.ClientPort;
        IPEndPoint clientDestination = new(message.PeerAddress, clientPort);
        byte[] innerReplyBytes = Dhcpv6MessageWriter.Write(innerReply);

        try
        {
            await _outboundSocket.SendToAsync(
                innerReplyBytes,
                clientDestination,
                cancellationToken
            );
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                exception,
                "Error relaying a DHCPv6 reply down to {Destination}.",
                clientDestination
            );
        }

        // This class always sends every real datagram itself, through its own outbound socket --
        // there is never a reply for a caller (Dhcpv6UdpListener's own generic relay dispatch, or
        // this class's own ExecuteAsync loop above) to send back on this class's behalf.
        return null;
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _outboundSocket.Dispose();
        base.Dispose();
    }
}
