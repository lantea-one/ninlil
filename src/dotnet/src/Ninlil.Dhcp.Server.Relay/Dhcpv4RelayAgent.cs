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
///     This class is the real RFC 3046 DHCPv4 relay agent: the alternative
///     <see cref="IDhcpv4RequestHandler" /> implementation <see cref="Dhcpv4UdpListener" />
///     dispatches every decoded datagram to when this server is configured to relay rather than
///     answer locally (see <see cref="Dhcpv4RelayOptions" />'s own doc comment for why the two are
///     mutually exclusive).
///     <para>
///         <b>Why this does not fit the "return a reply, let the listener send it" shape every
///         other <see cref="IDhcpv4RequestHandler" /> implementation uses</b>: that shape assumes
///         a single request synchronously produces a single reply addressed back to whoever sent
///         the request -- true for a local engine answering from its own scope/lease state, but
///         not for a relay. A relay's own two jobs are genuinely asymmetric: forwarding a client's
///         request means sending a <i>different</i> datagram (hops incremented, <c>giaddr</c>
///         set, Relay Agent Information possibly added) to a completely different destination
///         (the upstream server, not back to the client), and relaying a server's own reply back
///         down means sending yet another datagram to a third destination (the client's own
///         subnet), computed by an entirely different rule than either of the other two sends.
///         This class therefore always returns <see langword="null" /> to
///         <see cref="Dhcpv4UdpListener" /> -- telling it there is nothing more for it to send --
///         and performs every real send itself, through its own dedicated outbound socket.
///     </para>
///     <para>
///         <b>How the same listening socket carries both directions</b>: <see cref="Dhcpv4UdpListener" />
///         binds one socket on the DHCP server port and dispatches every decoded datagram to this
///         class, regardless of its own "op" field. A client's own DISCOVER/REQUEST/etc. arrives
///         with <see cref="Dhcpv4OperationCodeEnum.BootRequest" />, and is forwarded upstream, per
///         <see cref="RelayRequestUpstreamAsync" />. Once this relay has set its own address into
///         <c>giaddr</c>, RFC 2131 Section 4.1 has the upstream server unicast its own OFFER/ACK/
///         NAK reply to "the 'DHCP server' port on the BOOTP relay agent whose address appears in
///         'giaddr'" -- that is, to this relay's own address, on the very same server port this
///         same listening socket is already bound to. That reply therefore arrives back through
///         the exact same socket, decoded with <see cref="Dhcpv4OperationCodeEnum.BootReply" />,
///         and is relayed on down to the client by <see cref="RelayReplyDownToClientAsync" /> --
///         no second listening socket is needed for either direction.
///     </para>
/// </summary>
public sealed class Dhcpv4RelayAgent : IDhcpv4RequestHandler, IDisposable
{
    /// <summary>
    ///     This constant bounds how many times a request may already have been relayed (RFC 2131
    ///     Section 2's own "hops" field) before this relay refuses to forward it any further. RFC
    ///     2131 itself does not mandate a specific limit -- this is a deliberate, defensive
    ///     loop-prevention measure this project chooses, matching the same "fail safe rather than
    ///     become part of the problem" posture <c>Server.RateLimiting</c>'s own fail-open design
    ///     already documents for a different failure mode.
    /// </summary>
    private const byte MaxHops = 16;

    private readonly IOptions<Dhcpv4RelayOptions> _relayOptions;
    private readonly IOptions<Dhcpv4ProtocolOptions> _protocolOptions;
    private readonly IDhcpv4RelayAgentAddressProvider _addressProvider;
    private readonly ILogger<Dhcpv4RelayAgent> _logger;

    /// <summary>
    ///     This property contains the socket every outbound send (both the upstream-bound forward
    ///     and the client-bound relayed reply) goes out through. It is bound to an OS-assigned
    ///     ephemeral local port purely to send from -- neither upstream servers nor clients need
    ///     to reach it at any fixed port of its own, since a server's own reply always targets
    ///     <c>giaddr</c> at the well-known DHCP server port, which <see cref="Dhcpv4UdpListener" />'s
    ///     own separate listening socket already owns.
    /// </summary>
    private readonly Socket _outboundSocket;

    /// <param name="relayOptions">This relay's own upstream-server and Relay Agent Information configuration.</param>
    /// <param name="protocolOptions">The transport's own configured client port, used for a relayed reply's own destination.</param>
    /// <param name="addressProvider">This relay's own resolved <c>giaddr</c> address.</param>
    /// <param name="logger">The logger this relay's own forwarding decisions are recorded to.</param>
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

        // EnableBroadcast is needed for the downstream leg: relaying a reply back down to a
        // client that set its own broadcast flag (RelayReplyDownToClientAsync) sends to the
        // limited-broadcast address, exactly like Dhcpv4UdpListener's own server-side socket does
        // for the same reason.
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

        // This class always sends every real datagram itself, through its own outbound socket
        // (see this class's own doc comment for why) -- there is never anything left for
        // Dhcpv4UdpListener to send back to remoteEndPoint on this class's behalf.
        return null;
    }

    /// <summary>
    ///     This method implements the forwarding half of RFC 2131 Section 4.1's relay-agent
    ///     behavior: a client's (or an already-relayed, further-upstream-bound) request is
    ///     forwarded, unicast, to every configured upstream DHCPv4 server.
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

        // A defensive guard against a relay loop -- see MaxHops's own doc comment.
        if (request.Hops >= MaxHops)
        {
            _logger.LogWarning(
                "Dropping a DHCPv4 request whose own Hops count ({Hops}) has already reached this relay's defensive limit ({MaxHops}).",
                request.Hops,
                MaxHops
            );
            return;
        }

        // RFC 2131 Section 2 describes "hops" as a count of how many relay agents have already
        // forwarded a message -- every relay along the path increments it, regardless of whether
        // it is the first.
        request.Hops = (byte)(request.Hops + 1);

        // RFC 2131 Section 2 describes "giaddr" as filled in by "the first BOOTP relay agent"
        // that forwards a client's message -- a later relay in a multi-hop chain must leave an
        // already-set value untouched, since it identifies the client's own subnet (where the
        // first relay sits), not any later relay's own, different subnet.
        if (request.RelayAgentIpAddress.Equals(IPAddress.Any))
            request.RelayAgentIpAddress = _addressProvider.Value;

        // RFC 3046 Section 1 describes the Relay Agent Information option as added by "the relay
        // agent closest to the DHCP client" to tell its own upstream server which of its
        // interfaces/circuits that client is on. This project always sets (replacing any value
        // already present, including one a directly-attached client tried to forge -- a client
        // has no legitimate reason to send this option itself) its own configured Circuit ID/
        // Remote ID when at least one is configured, and otherwise leaves whatever Relay Agent
        // Information a further-upstream chained relay might have already added completely
        // untouched.
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

        // A real relay agent conventionally forwards to every configured upstream server at once
        // (see Dhcpv4RelayOptions.UpstreamServerAddresses's own doc comment) -- whichever one has
        // an address available replies first.
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
    ///     This method implements the return half of RFC 2131 Section 4.1's relay-agent behavior:
    ///     relaying an upstream server's own OFFER/ACK/NAK back down to the original client.
    ///     <para>
    ///         <b>Re-derived directly from RFC 2131 Section 4.1's own text, not copied from
    ///         <c>Dhcpv4UdpListener.SendReplyAsync</c></b>: that method implements a
    ///         <i>server's</i> own reply-destination rule, whose very first branch is "if
    ///         'giaddr' is non-zero, send to the relay agent at 'giaddr'" -- a rule that exists
    ///         specifically to route a reply <i>to</i> a relay in the first place, and is
    ///         therefore meaningless from a relay's own vantage point once it has already received
    ///         that reply addressed to itself: the relay's own job here is exactly the situation
    ///         that same RFC section describes for a server whose "giaddr" is zero, applied to the
    ///         relay's own directly-connected client subnet rather than to whichever client's
    ///         request happened to reach an ordinary server directly. The remaining rules that
    ///         section describes for that case -- broadcast when the client's own broadcast flag
    ///         is set; otherwise unicast to the client's own address (<c>ciaddr</c>) if it already
    ///         has one; otherwise (a client with no address yet, and no broadcast flag set, which
    ///         RFC 2131 says should never happen for a well-behaved client precisely because it
    ///         cannot yet be reached any other way) unicast directly to the client's own hardware
    ///         address, bypassing ARP -- are exactly what this method applies. The final case
    ///         shares the same real, honestly-documented limitation
    ///         <c>Dhcpv4UdpListener.SendReplyAsync</c>'s own doc comment already
    ///         describes: a managed <see cref="Socket" /> has no portable way to bypass ARP
    ///         resolution, so this method falls back to broadcasting instead, which still reaches
    ///         a well-behaved client (since such a client always sets the broadcast flag for
    ///         exactly this reason) even though it is not a byte-for-byte implementation of that
    ///         final case.
    ///     </para>
    /// </summary>
    private async Task RelayReplyDownToClientAsync(
        Dhcpv4Message reply,
        CancellationToken cancellationToken
    )
    {
        // A reply that does not carry this relay's own giaddr back was never meant to be relayed
        // by this instance -- either a genuinely unrelated packet reached this socket, or a
        // hostile sender is attempting to inject a bogus reply. Either way, there is nothing
        // sensible to forward.
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
            // See this method's own doc comment for why this falls back to broadcasting rather
            // than a true ARP-bypassing unicast to the client's own hardware address.
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
