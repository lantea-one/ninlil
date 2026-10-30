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
using Ninlil.Dns.Common.Net;

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

    /// <summary>
    ///     This field contains the index of the interface that owns the relay's link-address, or
    ///     <see langword="null" /> when none does. It is the client link, so it is the scope of
    ///     a client's link-local address (see <see cref="BuildClientDestination" />).
    /// </summary>
    private readonly int? _clientInterfaceIndex;

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
        : this(relayOptions, protocolOptions, addressProvider, logger, FindInterfaceIndex) { }

    /// <summary>
    ///     This constructor lets a test replace the interface lookup, so the link-local scope
    ///     can be checked with an interface the machine really has.
    /// </summary>
    /// <param name="relayOptions">The upstream servers and Interface-ID settings.</param>
    /// <param name="protocolOptions">The transport options, read for the client port relayed replies go to.</param>
    /// <param name="addressProvider">The relay's link-address.</param>
    /// <param name="logger">The logger dropped and failed messages are recorded to.</param>
    /// <param name="interfaceIndexOf">Returns the index of the interface that owns an address, or <see langword="null" />.</param>
    internal Dhcpv6RelayAgent(
        IOptions<Dhcpv6RelayOptions> relayOptions,
        IOptions<Dhcpv6ProtocolOptions> protocolOptions,
        IDhcpv6RelayAgentAddressProvider addressProvider,
        ILogger<Dhcpv6RelayAgent> logger,
        Func<IPAddress, int?> interfaceIndexOf
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

        // The link-address names the client link (RFC 8415 Section 19.1.1), so its interface is the
        // one a client's link-local address belongs to. Found once: the address is fixed until a
        // restart.
        _clientInterfaceIndex = interfaceIndexOf(addressProvider.Value);

        if (_clientInterfaceIndex == null)
        {
            logger.LogWarning(
                "No network interface holds the relay link-address {LinkAddress}, so replies to DHCPv6 clients with a link-local address cannot be scoped to the clients' link and may not be delivered.",
                addressProvider.Value
            );
        }
    }

    /// <summary>
    ///     This method looks up the interface that owns an address with
    ///     <see cref="RelayInterfaceLocator" />.
    /// </summary>
    private static int? FindInterfaceIndex(IPAddress address) =>
        RelayInterfaceLocator.TryFind(address, out int interfaceIndex, out _)
            ? interfaceIndex
            : null;

    /// <summary>
    ///     This method builds the destination of a relayed reply. A link-local peer address
    ///     arrives with no scope, because the wire format has none, and a link-local address is
    ///     ambiguous without one: the send would fail or leave on the wrong link. It gets the
    ///     client link's interface index as its scope ID. Any other address is used as it is.
    /// </summary>
    /// <param name="peerAddress">The peer-address of the RELAY-REPL, the client's address.</param>
    /// <param name="clientInterfaceIndex">The index of the client link's interface, or <see langword="null" /> when unknown.</param>
    /// <param name="clientPort">The client UDP port.</param>
    internal static IPEndPoint BuildClientDestination(
        IPAddress peerAddress,
        int? clientInterfaceIndex,
        int clientPort
    )
    {
        if (
            clientInterfaceIndex is int scopeId
            && peerAddress.IsIPv6LinkLocal
            && peerAddress.ScopeId == 0
        )
            return new IPEndPoint(
                new IPAddress(peerAddress.GetAddressBytes(), scopeId),
                clientPort
            );

        return new IPEndPoint(peerAddress, clientPort);
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
        // handled, since chained relays are not supported.
        if (message.MessageType != Dhcpv6MessageTypeEnum.RelayReply)
        {
            _logger.LogDebug(
                "Ignoring a {MessageType} received on this relay's own outbound socket from {RemoteEndPoint}; only a RELAY-REPL is ever expected here.",
                message.MessageType,
                remoteEndPoint
            );
            return null;
        }

        // Any host that can reach this relay's sockets could send a RELAY-REPL, and the inner
        // message goes to the peer-address it names, so only the configured upstream servers are
        // trusted to answer.
        if (!IsConfiguredUpstream(remoteEndPoint.Address))
        {
            _logger.LogDebug(
                "Ignoring a RELAY-REPL from {RemoteEndPoint}, which is not a configured upstream server.",
                remoteEndPoint
            );
            return null;
        }

        // The server copies link-address from the RELAY-FORW (RFC 8415 Section 9.2), so a
        // different value means the packet does not answer a message this relay sent.
        if (!ClientAddress.IsSameAddress(message.LinkAddress, _addressProvider.Value))
        {
            _logger.LogDebug(
                "Ignoring a RELAY-REPL from {RemoteEndPoint} whose own link-address ({LinkAddress}) does not match this relay's ({RelayAddress}).",
                remoteEndPoint,
                message.LinkAddress,
                _addressProvider.Value
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

        // Only a message a server sends to a client is relayed; the other types (SOLICIT,
        // REQUEST and so on) are client messages, which a server never wraps in a RELAY-REPL.
        if (
            innerReply.MessageType
            is not (
                Dhcpv6MessageTypeEnum.Advertise
                or Dhcpv6MessageTypeEnum.Reply
                or Dhcpv6MessageTypeEnum.Reconfigure
            )
        )
        {
            _logger.LogDebug(
                "Discarding a RELAY-REPL from {RemoteEndPoint} whose own nested message is a {MessageType}, which a server does not send to a client.",
                remoteEndPoint,
                innerReply.MessageType
            );
            return null;
        }

        // The server copies peer-address from the RELAY-FORW, so it is the client's address. The
        // relay sends the unwrapped message there (RFC 8415). The wire address carries no scope
        // ID, so a link-local one is scoped with the client link's interface.
        int clientPort = _protocolOptions.Value.ClientPort;
        IPEndPoint clientDestination = BuildClientDestination(
            message.PeerAddress,
            _clientInterfaceIndex,
            clientPort
        );
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

    /// <summary>
    ///     This method says whether an address is one of the configured upstream servers. Entries
    ///     that do not parse or are not IPv6 never match; zone ids are ignored.
    /// </summary>
    private bool IsConfiguredUpstream(IPAddress sourceAddress)
    {
        foreach (string upstreamAddressText in _relayOptions.Value.UpstreamServerAddresses ?? [])
        {
            if (
                IPAddress.TryParse(upstreamAddressText, out IPAddress upstreamAddress)
                && upstreamAddress.AddressFamily == AddressFamily.InterNetworkV6
                && ClientAddress.IsSameAddress(upstreamAddress, sourceAddress)
            )
                return true;
        }

        return false;
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _outboundSocket.Dispose();
        base.Dispose();
    }
}
