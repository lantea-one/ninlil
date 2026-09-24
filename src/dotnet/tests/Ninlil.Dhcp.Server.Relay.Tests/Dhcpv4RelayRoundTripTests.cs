using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Protocol.Dhcpv4;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Relay.Tests;

/// <summary>
///     This test proves the real <see cref="Dhcpv4RelayAgent" /> and the real
///     <see cref="Dhcpv4UdpListener" /> genuinely work together over real UDP sockets, both
///     directions: a real "client" socket's own DISCOVER genuinely reaches a fake "upstream
///     server" socket with <c>hops</c>/<c>giaddr</c> correctly set, and that fake upstream's own
///     crafted OFFER genuinely gets relayed back down and is received by that same "client"
///     socket.
///     <para>
///         Both this relay's own listening address and the client's own address use ordinary,
///         unprivileged loopback addresses/ports rather than a real network interface and the
///         real IANA-assigned 67/68 ports -- the real privileged-port bind was already, separately
///         proven for this project's shared <see cref="Dhcpv4UdpListener" /> transport during
///         its own development; this test
///         proves the relay-specific protocol logic layered on top of it, which is identical
///         regardless of which port number is actually bound.
///     </para>
///     <para>
///         The crafted OFFER deliberately sets <c>ciaddr</c> and clears the broadcast flag, rather
///         than mirroring a real OFFER's own usual all-zero-address/broadcast-flag-set shape --
///         the same deliberate simplification <c>Ninlil.Dhcp.Protocol.Dhcpv4.Tests
///         .Dhcpv4UdpRoundTripTests</c> already establishes, forcing a deterministic unicast
///         delivery path rather than depending on this sandboxed environment's own, possibly
///         restricted, UDP broadcast delivery behavior.
///     </para>
/// </summary>
public sealed class Dhcpv4RelayRoundTripTests
{
    [Fact]
    public async Task Discover_IsForwardedUpstream_AndTheUpstreamsOfferIsRelayedBackToTheClient()
    {
        int relayListenPort = GetFreeUdpPort();
        int relayClientPort = GetFreeUdpPort();
        int upstreamPort = GetFreeUdpPort();
        IPAddress relayAgentAddress = IPAddress.Parse("127.0.0.21");

        Dhcpv4RelayOptions relayOptions = new()
        {
            Enable = true,
            UpstreamServerAddresses = ["127.0.0.1"],
            UpstreamServerPort = upstreamPort,
        };
        Dhcpv4ProtocolOptions protocolOptions = new()
        {
            Enable = true,
            ServerPort = relayListenPort,
            ClientPort = relayClientPort,
        };
        Dhcpv4RelayAgent agent = new(
            Options.Create(relayOptions),
            Options.Create(protocolOptions),
            new FixedDhcpv4RelayAgentAddressProvider(relayAgentAddress),
            NullLogger<Dhcpv4RelayAgent>.Instance
        );
        Dhcpv4UdpListener listener = new(
            agent,
            Options.Create(protocolOptions),
            NullLogger<Dhcpv4UdpListener>.Instance
        );

        await listener.StartAsync(CancellationToken.None);

        using UdpClient upstreamSocket = new(new IPEndPoint(IPAddress.Loopback, upstreamPort));
        using UdpClient clientSocket = new(new IPEndPoint(IPAddress.Loopback, relayClientPort));

        try
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);

            while (!listener.IsListening && DateTime.UtcNow < deadline)
                await Task.Delay(10);

            Assert.True(listener.IsListening, "The relay's own listener never finished binding.");

            // Step 1: the client's own broadcast DISCOVER "reaches the relay" (sent directly, to
            // this relay's own listening port, standing in for the relay having genuinely received
            // a real local-subnet broadcast).
            Dhcpv4Message discover = new()
            {
                OperationCode = Dhcpv4OperationCodeEnum.BootRequest,
                TransactionId = 0xAABB_CCDD,
                HardwareAddressLength = 6,
                ClientHardwareAddress = [0x02, 0x00, 0x00, 0x00, 0x00, 0x10],
                BroadcastFlagSet = true,
            };
            discover.SetMessageType(Dhcpv4MessageTypeEnum.Discover);

            byte[] discoverBytes = Dhcpv4MessageWriter.Write(discover);
            await clientSocket.SendAsync(
                discoverBytes,
                discoverBytes.Length,
                new IPEndPoint(IPAddress.Loopback, relayListenPort)
            );

            // Step 2: the fake upstream server genuinely receives the forwarded request, with
            // hops/giaddr set correctly by the real relay agent.
            using CancellationTokenSource upstreamReceiveTimeout = new(TimeSpan.FromSeconds(5));
            UdpReceiveResult forwardedResult = await upstreamSocket.ReceiveAsync(
                upstreamReceiveTimeout.Token
            );

            Assert.True(
                Dhcpv4MessageReader.TryRead(
                    forwardedResult.Buffer,
                    out Dhcpv4Message forwarded,
                    out string forwardFailureReason
                ),
                forwardFailureReason
            );
            Assert.True(forwarded.TryGetMessageType(out Dhcpv4MessageTypeEnum forwardedType));
            Assert.Equal(Dhcpv4MessageTypeEnum.Discover, forwardedType);
            Assert.Equal(discover.TransactionId, forwarded.TransactionId);
            Assert.Equal(relayAgentAddress, forwarded.RelayAgentIpAddress);
            Assert.Equal((byte)1, forwarded.Hops);

            // Step 3: the fake upstream server crafts an OFFER and sends it to giaddr at the DHCP
            // server port -- exactly where RFC 2131 Section 4.1 says a server unicasts a reply
            // through a relay agent, and exactly the same address:port this relay's own listener
            // is already bound to.
            Dhcpv4Message offer = new()
            {
                OperationCode = Dhcpv4OperationCodeEnum.BootReply,
                TransactionId = forwarded.TransactionId,
                YourIpAddress = IPAddress.Parse("203.0.113.77"),
                RelayAgentIpAddress = forwarded.RelayAgentIpAddress,
                ClientHardwareAddress = forwarded.ClientHardwareAddress,
                HardwareAddressLength = forwarded.HardwareAddressLength,
                // See this class's own doc comment for why ciaddr/broadcast are set this way
                // rather than mirroring a real OFFER's own usual shape.
                ClientIpAddress = IPAddress.Loopback,
                BroadcastFlagSet = false,
            };
            offer.SetMessageType(Dhcpv4MessageTypeEnum.Offer);

            byte[] offerBytes = Dhcpv4MessageWriter.Write(offer);
            await upstreamSocket.SendAsync(
                offerBytes,
                offerBytes.Length,
                new IPEndPoint(relayAgentAddress, relayListenPort)
            );

            // Step 4: the same relay agent, still listening on that one shared socket, receives
            // this BootReply-typed datagram and relays it on down to the original client.
            using CancellationTokenSource clientReceiveTimeout = new(TimeSpan.FromSeconds(5));
            UdpReceiveResult relayedResult = await clientSocket.ReceiveAsync(
                clientReceiveTimeout.Token
            );

            Assert.True(
                Dhcpv4MessageReader.TryRead(
                    relayedResult.Buffer,
                    out Dhcpv4Message relayedOffer,
                    out string relayFailureReason
                ),
                relayFailureReason
            );
            Assert.True(relayedOffer.TryGetMessageType(out Dhcpv4MessageTypeEnum relayedType));
            Assert.Equal(Dhcpv4MessageTypeEnum.Offer, relayedType);
            Assert.Equal(discover.TransactionId, relayedOffer.TransactionId);
            Assert.Equal(offer.YourIpAddress, relayedOffer.YourIpAddress);
        }
        finally
        {
            await listener.StopAsync(CancellationToken.None);
            agent.Dispose();
        }
    }

    /// <summary>
    ///     This method obtains a currently-unused, unprivileged UDP port by asking the operating
    ///     system to assign an ephemeral one, reading it back, and immediately releasing it --
    ///     good enough for a test that binds it again itself moments later, with only a small,
    ///     accepted race window against something else on the same machine grabbing it first.
    /// </summary>
    private static int GetFreeUdpPort()
    {
        using UdpClient probe = new(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint).Port;
    }
}
