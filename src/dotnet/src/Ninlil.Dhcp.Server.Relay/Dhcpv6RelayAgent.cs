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
///     This class is the DHCPv6 relay agent (RFC 8415): the
///     <see cref="IDhcpv6RequestHandler" /> and <see cref="IDhcpv6RelayMessageHandler" /> that
///     <see cref="Dhcpv6UdpListener" /> uses instead of the local engine when
///     <see cref="Dhcpv6RelayOptions.Enable" /> is set. It is the DHCPv6 counterpart of
///     <see cref="Dhcpv4RelayAgent" />.
///     <para>
///         <b>It has a second receive loop.</b> A DHCPv4 server sends its reply to the relay's
///         server port, where the listener already listens. A DHCPv6 server sends the RELAY-REPL
///         to the source address and port of the RELAY-FORW, and this class sends RELAY-FORW
///         from the ephemeral port of <see cref="_outboundSocket" />. So the class is also a
///         <see cref="BackgroundService" /> whose <see cref="ExecuteAsync" /> receives on that
///         socket. The relay handling is also reachable from the listener: a RELAY-REPL arriving
///         on the server port is dispatched to <see cref="HandleRelayMessageAsync" /> as well.
///     </para>
///     <para>
///         <b>It sends everything itself.</b> Client messages arrive from the listener through
///         <see cref="HandleRequestAsync" />, are wrapped in a RELAY-FORW and sent upstream.
///         Replies are unwrapped and sent to the client. Both handler methods return
///         <see langword="null" />, leaving the listener nothing to send.
///     </para>
/// </summary>
public sealed class Dhcpv6RelayAgent
    : BackgroundService,
        IDhcpv6RequestHandler,
        IDhcpv6RelayMessageHandler
{
    /// <summary>
    ///     This constant contains the receive buffer size: the largest UDP payload, as in
    ///     <c>Dhcpv6UdpListener.MaximumDatagramSize</c>.
    /// </summary>
    private const int MaximumDatagramSize = 65507;

    /// <summary>
    ///     This constant is the largest "hop-count" a received RELAY-REPL may carry. This relay
    ///     always sends RELAY-FORW with hop-count 0 (only one hop is supported; see
    ///     <see cref="Dhcpv6RelayMessage" />), and the server copies it into the reply, so a higher
    ///     value means the packet is not an answer to this relay. The limit of 16 matches
    ///     <c>Dhcpv4RelayAgent.MaxHops</c>, so values from 1 to 16 are still accepted.
    /// </summary>
    private const byte MaxHops = 16;

    private readonly IOptions<Dhcpv6RelayOptions> _relayOptions;
    private readonly IOptions<Dhcpv6ProtocolOptions> _protocolOptions;
    private readonly IDhcpv6RelayAgentAddressProvider _addressProvider;
    private readonly ILogger<Dhcpv6RelayAgent> _logger;

    /// <summary>
    ///     This field contains the socket RELAY-FORW messages and relayed replies are sent from,
    ///     and on which <see cref="ExecuteAsync" /> receives RELAY-REPL messages (see the class
    ///     summary). It is bound to the wildcard address and an ephemeral port.
    /// </summary>
    private readonly Socket _outboundSocket;

    /// <param name="relayOptions">The upstream servers and Interface-ID settings.</param>
    /// <param name="protocolOptions">The transport options, read for the client port relayed replies go to.</param>
    /// <param name="addressProvider">The relay's link-address.</param>
    /// <param name="logger">The logger dropped and failed messages are recorded to.</param>
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
            // Program.cs only registers the relay when this flag is set, so this is a safety
            // check in case the class is registered some other way.
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
                // The result is always null: the method sends the reply itself.
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

        // Everything has been sent through the outbound socket (see the class summary); the
        // listener has nothing to send.
        return null;
    }

    /// <summary>
    ///     This method wraps a client message in a RELAY-FORW, with the client's source address as
    ///     peer-address and the optional Interface-ID, and sends it by unicast to every configured
    ///     upstream server. It drops the message when no upstream is configured or the envelope
    ///     cannot be encoded.
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
            // Only one hop is supported (see Dhcpv6RelayMessage), so this relay is always the
            // first.
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

        // Send to every upstream server (see Dhcpv6RelayOptions.UpstreamServerAddresses).
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
        // Only a RELAY-REPL is an answer to this relay. A RELAY-FORW from another relay is not
        // handled, since chained relays are not supported. The sender is not checked against
        // the upstream servers.
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

        // The server copies peer-address from the RELAY-FORW, so it is the client's address. The
        // relay sends the unwrapped message there (RFC 8415). The address carries no scope ID.
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

        // The reply has been sent here; neither the listener nor ExecuteAsync has anything to
        // send.
        return null;
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _outboundSocket.Dispose();
        base.Dispose();
    }
}
