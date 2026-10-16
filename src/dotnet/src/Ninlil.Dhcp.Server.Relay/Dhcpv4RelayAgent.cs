using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Protocol.Dhcpv4;
using Ninlil.Dhcp.Server.Relay.Identity;
using Ninlil.Dns.Common.Net;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Relay;

/// <summary>
///     This class is the DHCPv4 relay agent (RFC 2131 Section 4.1, with RFC 3046 Relay Agent
///     Information): the <see cref="IDhcpv4RequestHandler" /> that <see cref="Dhcpv4UdpListener" />
///     uses instead of the local engine when <see cref="Dhcpv4RelayOptions.Enable" /> is set.
///     <para>
///         <b>It sends everything itself.</b> Other handlers return a reply for the listener to
///         send back to the sender, but a relay sends a modified request (hops incremented,
///         <c>giaddr</c> set, Relay Agent Information possibly added) to the upstream servers, and
///         later sends the server's reply to the client subnet by a different rule.
///         <see cref="HandleRequestAsync" /> therefore always returns <see langword="null" /> and
///         sends through separate sockets.
///     </para>
///     <para>
///         <b>Two sending sockets.</b> Forwarded requests leave through
///         <see cref="_outboundSocket" />, which the routing table steers to the upstream
///         servers. Replies to clients leave through <see cref="_clientSideSocket" />, which is
///         bound to <c>giaddr</c> and, on Linux, to the interface that owns it. A limited
///         broadcast (255.255.255.255) is not routed, so without that binding it would leave on
///         the interface of the default route, which on a relay is often the one facing the
///         servers. The source port of that socket stays ephemeral: binding port 67 on
///         <c>giaddr</c> would take the servers' replies away from the listener's wildcard
///         socket, and RFC 2131 Section 4.1 names only the destination port.
///     </para>
///     <para>
///         <b>One listening socket for both directions.</b> The listener passes every datagram
///         here. A client request (<see cref="Dhcpv4OperationCodeEnum.BootRequest" />) is forwarded
///         by <see cref="RelayRequestUpstreamAsync" />. Because the relay set <c>giaddr</c>, RFC
///         2131 Section 4.1 has the server send its reply to "the 'DHCP server' port on the BOOTP
///         relay agent whose address appears in 'giaddr'", the port the listener is bound to, so
///         the reply (<see cref="Dhcpv4OperationCodeEnum.BootReply" />) arrives on the same socket
///         and goes to <see cref="RelayReplyDownToClientAsync" />.
///     </para>
/// </summary>
public sealed class Dhcpv4RelayAgent : IDhcpv4RequestHandler, IDisposable
{
    /// <summary>
    ///     This constant is the "hops" value (RFC 2131 Section 2) at which a request is no longer
    ///     forwarded, a guard against relay loops. RFC 2131 sets no limit; 16 is this server's
    ///     choice.
    /// </summary>
    private const byte MaxHops = 16;

    private readonly IOptions<Dhcpv4RelayOptions> _relayOptions;
    private readonly IOptions<Dhcpv4ProtocolOptions> _protocolOptions;
    private readonly IDhcpv4RelayAgentAddressProvider _addressProvider;
    private readonly ILogger<Dhcpv4RelayAgent> _logger;

    /// <summary>
    ///     This field contains the socket forwarded requests are sent from. It is bound to the
    ///     wildcard address and an ephemeral port, since nothing needs to reach it: servers reply
    ///     to <c>giaddr</c> on the server port, which the listener's socket owns. The operating
    ///     system picks the outgoing interface by routing. It also carries replies to clients
    ///     when <see cref="_clientSideSocket" /> is <see langword="null" />.
    /// </summary>
    private readonly Socket _outboundSocket;

    /// <summary>
    ///     This field contains the socket replies to clients are sent from: bound to
    ///     <c>giaddr</c> and an ephemeral port, with broadcast enabled and, on Linux, bound to
    ///     the interface that owns <c>giaddr</c>. It is <see langword="null" /> when
    ///     <c>giaddr</c> is not an address of this host or the binding fails; replies then use
    ///     <see cref="_outboundSocket" /> and one Warning says so.
    /// </summary>
    private readonly Socket _clientSideSocket;

    /// <summary>
    ///     These fields cache the encoded IDs (see <see cref="GetRelayAgentIds" />). Concurrent
    ///     first requests may both compute them; the result is identical, so the race is benign
    ///     apart from a possible second error log line.
    /// </summary>
    private volatile bool _relayAgentIdsResolved;

    private byte[] _encodedCircuitId;
    private byte[] _encodedRemoteId;

    /// <param name="relayOptions">The upstream servers and Relay Agent Information settings.</param>
    /// <param name="protocolOptions">The transport options, read for the client port relayed replies go to.</param>
    /// <param name="addressProvider">The relay's <c>giaddr</c> address.</param>
    /// <param name="logger">The logger dropped and failed sends are recorded to.</param>
    public Dhcpv4RelayAgent(
        IOptions<Dhcpv4RelayOptions> relayOptions,
        IOptions<Dhcpv4ProtocolOptions> protocolOptions,
        IDhcpv4RelayAgentAddressProvider addressProvider,
        ILogger<Dhcpv4RelayAgent> logger
    )
    {
        _relayOptions = relayOptions;
        _protocolOptions = protocolOptions;
        _addressProvider = addressProvider;
        _logger = logger;

        // EnableBroadcast is needed to relay replies to the limited-broadcast address, as the
        // listener's socket does.
        _outboundSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        {
            EnableBroadcast = true,
        };
        _outboundSocket.Bind(new IPEndPoint(IPAddress.Any, 0));

        _clientSideSocket = CreateClientSideSocket(addressProvider.Value, logger);
    }

    /// <summary>
    ///     This method creates the socket that sends replies down to clients (see
    ///     <see cref="_clientSideSocket" />). It never throws: a relay whose giaddr is not local,
    ///     for example one behind address translation, still relays through the outbound socket.
    /// </summary>
    /// <returns>The bound socket, or <see langword="null" /> after one Warning when it cannot be made.</returns>
    private static Socket CreateClientSideSocket(IPAddress giaddr, ILogger logger)
    {
        Socket socket = null;

        try
        {
            if (!RelayInterfaceLocator.TryFind(giaddr, out _, out string interfaceName))
            {
                logger.LogWarning(
                    "No network interface holds the relay address {Giaddr}, so replies to DHCPv4 clients leave through the interface the routing table picks, which may not be the clients' link.",
                    giaddr
                );
                return null;
            }

            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            {
                EnableBroadcast = true,
            };

            // SO_BINDTODEVICE (socket(7)) makes the socket send and receive only on that
            // interface, so a limited broadcast leaves on the clients' link. It must be set
            // before Bind. The option value is the interface name with a terminating NUL.
            // Linux only: other systems send from the bound source address alone.
            if (OperatingSystem.IsLinux())
            {
                const int SolSocket = 1;
                const int SoBindToDevice = 25;
                socket.SetRawSocketOption(
                    SolSocket,
                    SoBindToDevice,
                    Encoding.ASCII.GetBytes(interfaceName + '\0')
                );
            }

            // The source address of a reply is the relay's address on the client link.
            socket.Bind(new IPEndPoint(giaddr, 0));
            return socket;
        }
        catch (Exception exception) when (exception is SocketException or ArgumentException)
        {
            logger.LogWarning(
                exception,
                "Cannot bind a socket to the relay address {Giaddr} on its interface, so replies to DHCPv4 clients leave through the interface the routing table picks, which may not be the clients' link.",
                giaddr
            );
            socket?.Dispose();
            return null;
        }
    }

    /// <inheritdoc />
    public async ValueTask<Dhcpv4Message> HandleRequestAsync(
        Dhcpv4Message request,
        IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken
    )
    {
        switch (request.OperationCode)
        {
            case Dhcpv4OperationCodeEnum.BootRequest:
                await RelayRequestUpstreamAsync(request, cancellationToken);
                break;
            case Dhcpv4OperationCodeEnum.BootReply:
                await RelayReplyDownToClientAsync(request, remoteEndPoint, cancellationToken);
                break;
        }

        // Everything has been sent through the agent's own sockets (see the class summary); the
        // listener has nothing to send.
        return null;
    }

    /// <summary>
    ///     This method forwards a request, from a client or from another relay, by unicast to
    ///     every configured upstream server (RFC 2131 Section 4.1). It drops the request when no
    ///     upstream is configured or the hop count has reached <see cref="MaxHops" />.
    /// </summary>
    private async Task RelayRequestUpstreamAsync(
        Dhcpv4Message request,
        CancellationToken cancellationToken
    )
    {
        List<string> upstreamAddresses = _relayOptions.Value.UpstreamServerAddresses;

        if (upstreamAddresses is not { Count: > 0 })
        {
            _logger.LogWarning(
                "Dropping a DHCPv4 request this relay would otherwise forward: no upstream server addresses are configured ({OptionName}).",
                $"{Dhcpv4RelayOptions.SectionName}:{nameof(Dhcpv4RelayOptions.UpstreamServerAddresses)}"
            );
            return;
        }

        // Guard against relay loops (see MaxHops).
        if (request.Hops >= MaxHops)
        {
            _logger.LogWarning(
                "Dropping a DHCPv4 request whose own Hops count ({Hops}) has already reached this relay's defensive limit ({MaxHops}).",
                request.Hops,
                MaxHops
            );
            return;
        }

        // RFC 3046 Section 2.1.1: a request with no giaddr reached this relay straight from a
        // client, and a client must not send Relay Agent Information. Drop it as forged, whether
        // or not this relay adds IDs of its own.
        if (
            request.RelayAgentIpAddress.Equals(IPAddress.Any)
            && request.TryGetRawOption((byte)Dhcpv4OptionTagEnum.RelayAgentInformation, out _)
        )
        {
            _logger.LogDebug(
                "Dropping a DHCPv4 request from a client that carries Relay Agent Information (RFC 3046 Section 2.1.1)."
            );
            return;
        }

        // Every relay on the path increments "hops" (RFC 2131 Section 2).
        request.Hops = (byte)(request.Hops + 1);

        // Only "the first BOOTP relay agent" sets "giaddr" (RFC 2131 Section 2); a later relay
        // keeps it, since it names the client's subnet.
        if (request.RelayAgentIpAddress.Equals(IPAddress.Any))
            request.RelayAgentIpAddress = _addressProvider.Value;

        // RFC 3046 Section 1: the Relay Agent Information option is added by "the relay agent
        // closest to the DHCP client". With a Circuit ID or Remote ID configured, set the option,
        // replacing any value already present. A request that carries the option and has no
        // giaddr came straight from a client, which must not send one, was dropped above.
        byte[] wireBytes;

        try
        {
            if (GetRelayAgentIds(out byte[] circuitId, out byte[] remoteId))
                request.SetRelayAgentInformation(circuitId, remoteId);

            wireBytes = Dhcpv4MessageWriter.Write(request);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Dropping a DHCPv4 request that cannot be encoded for the upstream servers."
            );
            return;
        }

        int upstreamPort = _relayOptions.Value.UpstreamServerPort;

        // Send to every upstream server (see Dhcpv4RelayOptions.UpstreamServerAddresses).
        foreach (string upstreamAddressText in upstreamAddresses)
        {
            if (!IPAddress.TryParse(upstreamAddressText, out IPAddress upstreamAddress))
            {
                _logger.LogWarning(
                    "Skipping an invalid configured upstream DHCPv4 server address: '{Address}'.",
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
                    "Error forwarding a DHCPv4 request to upstream server {Address}.",
                    upstreamAddress
                );
            }
        }
    }

    /// <summary>
    ///     This method encodes the configured Circuit ID and Remote ID, once, on the first request.
    ///     The options are fixed until a restart, so one validation serves every request. Invalid
    ///     IDs (a hand-edited file; the settings API rejects them) are logged as an error once
    ///     and requests are then forwarded without the option rather than dropped, so a bad ID
    ///     does not stop DHCP for the whole relayed subnet.
    /// </summary>
    /// <returns>True when the relay adds Relay Agent Information to forwarded requests.</returns>
    private bool GetRelayAgentIds(out byte[] circuitId, out byte[] remoteId)
    {
        if (!_relayAgentIdsResolved)
        {
            string circuitText = _relayOptions.Value.RelayAgentCircuitId;
            string remoteText = _relayOptions.Value.RelayAgentRemoteId;

            if (
                !Dhcpv4RelayOptions.TryValidateRelayAgentIds(
                    circuitText,
                    remoteText,
                    out string error
                )
            )
            {
                _logger.LogError(
                    "The configured Circuit ID or Remote ID is invalid ({Reason}); requests are forwarded without Relay Agent Information.",
                    error
                );
                _encodedCircuitId = null;
                _encodedRemoteId = null;
            }
            else
            {
                _encodedCircuitId = string.IsNullOrEmpty(circuitText)
                    ? null
                    : Encoding.ASCII.GetBytes(circuitText);
                _encodedRemoteId = string.IsNullOrEmpty(remoteText)
                    ? null
                    : Encoding.ASCII.GetBytes(remoteText);
            }

            _relayAgentIdsResolved = true;
        }

        circuitId = _encodedCircuitId;
        remoteId = _encodedRemoteId;
        return circuitId != null || remoteId != null;
    }

    /// <summary>
    ///     This method relays an upstream server's OFFER, ACK or NAK to the client (RFC 2131
    ///     Section 4.1). A reply is ignored unless it comes from one of the configured upstream
    ///     servers (by source address; the source port is not checked, since a server may answer
    ///     from a different socket or sit behind address translation) and its <c>giaddr</c> is
    ///     this relay's address.
    ///     <para>
    ///         The destination rule is not the one in <c>Dhcpv4UdpListener.SendReplyAsync</c>,
    ///         whose first case (send to <c>giaddr</c>) is how the reply reached this relay. From
    ///         here the rules for a server with a zero <c>giaddr</c> apply: broadcast when the
    ///         broadcast flag is set, otherwise unicast to <c>ciaddr</c> if the client has one,
    ///         otherwise unicast to the client's hardware address without ARP. As in the listener,
    ///         that last case is replaced by a broadcast, since a managed <see cref="Socket" />
    ///         cannot bypass ARP; a client without an address still receives it. The reply leaves
    ///         through <see cref="_clientSideSocket" /> when there is one.
    ///     </para>
    /// </summary>
    private async Task RelayReplyDownToClientAsync(
        Dhcpv4Message reply,
        IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken
    )
    {
        // Any host that can reach the server port could send a BootReply, so only the
        // configured upstream servers are trusted to answer.
        if (!IsConfiguredUpstream(remoteEndPoint.Address))
        {
            _logger.LogDebug(
                "Ignoring a DHCPv4 reply from {RemoteAddress}, which is not a configured upstream server.",
                remoteEndPoint.Address
            );
            return;
        }

        // A reply without this relay's giaddr is not ours to relay: an unrelated packet or a
        // reply meant for another relay.
        if (!reply.RelayAgentIpAddress.Equals(_addressProvider.Value))
        {
            _logger.LogDebug(
                "Ignoring a DHCPv4 reply whose own giaddr ({Giaddr}) does not match this relay's own address ({RelayAddress}).",
                reply.RelayAgentIpAddress,
                _addressProvider.Value
            );
            return;
        }

        int clientPort = _protocolOptions.Value.ClientPort;
        IPEndPoint destination;

        if (reply.BroadcastFlagSet)
        {
            destination = new IPEndPoint(IPAddress.Broadcast, clientPort);
        }
        else if (!reply.ClientIpAddress.Equals(IPAddress.Any))
        {
            destination = new IPEndPoint(reply.ClientIpAddress, clientPort);
        }
        else
        {
            // Broadcast instead of an ARP-free unicast to the hardware address (see the summary).
            destination = new IPEndPoint(IPAddress.Broadcast, clientPort);
        }

        try
        {
            byte[] wireBytes = Dhcpv4MessageWriter.Write(reply);
            await (_clientSideSocket ?? _outboundSocket).SendToAsync(
                wireBytes,
                destination,
                cancellationToken
            );
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                exception,
                "Error encoding or relaying a DHCPv4 reply down to {Destination}.",
                destination
            );
        }
    }

    /// <summary>
    ///     This method says whether an address is one of the configured upstream servers. Entries
    ///     that do not parse or are not IPv4 never match.
    /// </summary>
    private bool IsConfiguredUpstream(IPAddress sourceAddress)
    {
        foreach (string upstreamAddressText in _relayOptions.Value.UpstreamServerAddresses ?? [])
        {
            if (
                IPAddress.TryParse(upstreamAddressText, out IPAddress upstreamAddress)
                && upstreamAddress.AddressFamily == AddressFamily.InterNetwork
                && ClientAddress.IsSameAddress(upstreamAddress, sourceAddress)
            )
                return true;
        }

        return false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _outboundSocket.Dispose();
        _clientSideSocket?.Dispose();
    }
}
