using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Protocol.Dhcpv4;
using Ninlil.Dhcp.Server.Relay.Identity;

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
///         sends through a separate outbound socket.
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
    ///     This field contains the socket both forwarded requests and relayed replies are sent
    ///     from. It is bound to the wildcard address and an ephemeral port, since nothing needs to
    ///     reach it: servers reply to <c>giaddr</c> on the server port, which the listener's socket
    ///     owns. The operating system picks the outgoing interface by routing, including for
    ///     broadcasts.
    /// </summary>
    private readonly Socket _outboundSocket;

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
                await RelayReplyDownToClientAsync(request, cancellationToken);
                break;
        }

        // Everything has been sent through the outbound socket (see the class summary); the
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

        // Every relay on the path increments "hops" (RFC 2131 Section 2).
        request.Hops = (byte)(request.Hops + 1);

        // Only "the first BOOTP relay agent" sets "giaddr" (RFC 2131 Section 2); a later relay
        // keeps it, since it names the client's subnet.
        if (request.RelayAgentIpAddress.Equals(IPAddress.Any))
            request.RelayAgentIpAddress = _addressProvider.Value;

        // RFC 3046 Section 1: the Relay Agent Information option is added by "the relay agent
        // closest to the DHCP client". With a Circuit ID or Remote ID configured, set the option,
        // replacing any value already present (a client has no reason to send one, so it may be
        // forged). With neither, leave the option as received.
        if (
            !string.IsNullOrEmpty(_relayOptions.Value.RelayAgentCircuitId)
            || !string.IsNullOrEmpty(_relayOptions.Value.RelayAgentRemoteId)
        )
        {
            byte[] circuitId = string.IsNullOrEmpty(_relayOptions.Value.RelayAgentCircuitId)
                ? null
                : Encoding.ASCII.GetBytes(_relayOptions.Value.RelayAgentCircuitId);
            byte[] remoteId = string.IsNullOrEmpty(_relayOptions.Value.RelayAgentRemoteId)
                ? null
                : Encoding.ASCII.GetBytes(_relayOptions.Value.RelayAgentRemoteId);

            request.SetRelayAgentInformation(circuitId, remoteId);
        }

        byte[] wireBytes = Dhcpv4MessageWriter.Write(request);
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
    ///     This method relays an upstream server's OFFER, ACK or NAK to the client (RFC 2131
    ///     Section 4.1). A reply whose <c>giaddr</c> is not this relay's address is ignored.
    ///     <para>
    ///         The destination rule is not the one in <c>Dhcpv4UdpListener.SendReplyAsync</c>,
    ///         whose first case (send to <c>giaddr</c>) is how the reply reached this relay. From
    ///         here the rules for a server with a zero <c>giaddr</c> apply: broadcast when the
    ///         broadcast flag is set, otherwise unicast to <c>ciaddr</c> if the client has one,
    ///         otherwise unicast to the client's hardware address without ARP. As in the listener,
    ///         that last case is replaced by a broadcast, since a managed <see cref="Socket" />
    ///         cannot bypass ARP; a client without an address still receives it.
    ///     </para>
    /// </summary>
    private async Task RelayReplyDownToClientAsync(
        Dhcpv4Message reply,
        CancellationToken cancellationToken
    )
    {
        // A reply without this relay's giaddr is not ours to relay: an unrelated packet or a
        // forged reply. (The check does not stop a sender who knows the relay address.)
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

        byte[] wireBytes = Dhcpv4MessageWriter.Write(reply);

        try
        {
            await _outboundSocket.SendToAsync(wireBytes, destination, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                exception,
                "Error relaying a DHCPv4 reply down to {Destination}.",
                destination
            );
        }
    }

    /// <inheritdoc />
    public void Dispose() => _outboundSocket.Dispose();
}
