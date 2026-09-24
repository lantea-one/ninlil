using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Dhcpv4.Tests;

/// <summary>
///     This test proves the DHCPv4 UDP transport and wire codec genuinely work together over a
///     real socket, not merely as an in-memory byte-array round trip (that layer is already
///     covered exhaustively, and far faster, by <c>Ninlil.Dhcp.Common.Tests</c>): a real
///     <see cref="Dhcpv4UdpListener" /> binds a real UDP socket, a real <see cref="UdpClient" />
///     sends a real, wire-encoded DISCOVER datagram to it, a stub
///     <see cref="StubDhcpv4RequestHandler" /> answers with a canned OFFER, and this test's own
///     socket receives and decodes that reply back into a <see cref="Dhcpv4Message" />.
///     <para>
///         This runs on ordinary, unprivileged, dynamically-chosen high ports rather than the
///         real IANA-assigned 67/68 DHCP ports, since binding a port below 1024 needs root
///         privileges an automated test suite does not run with. The real privileged-port bind
///         was separately, manually proven with <c>sudo</c> in a throwaway scratchpad console app
///         during this feature's own development. This committed test instead proves the wire protocol and
///         socket-handling logic itself, which is identical regardless of which port number is
///         actually bound.
///     </para>
/// </summary>
public class Dhcpv4UdpRoundTripTests
{
    [Fact]
    public async Task DiscoverThenOffer_RoundTripsOverARealSocket()
    {
        int serverPort = GetFreeUdpPort();
        int clientPort = GetFreeUdpPort();

        StubDhcpv4RequestHandler handler = new();
        Dhcpv4ProtocolOptions options = new()
        {
            Enable = true,
            ServerPort = serverPort,
            ClientPort = clientPort,
        };
        Dhcpv4UdpListener listener = new(
            handler,
            Options.Create(options),
            NullLogger<Dhcpv4UdpListener>.Instance
        );

        await listener.StartAsync(CancellationToken.None);

        try
        {
            // Dhcpv4UdpListener.StartAsync binds synchronously before it returns, so IsListening
            // is already true here in practice -- this poll is a harmless belt-and-suspenders
            // guard against that timing detail ever changing, not a race this test depends on.
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);

            while (!listener.IsListening && DateTime.UtcNow < deadline)
                await Task.Delay(10);

            Assert.True(listener.IsListening, "The listener never finished binding its socket.");

            using UdpClient client = new(new IPEndPoint(IPAddress.Loopback, clientPort));

            byte[] clientHardwareAddress = [0x02, 0x00, 0x00, 0x00, 0x00, 0x01];

            Dhcpv4Message discover = new()
            {
                OperationCode = Dhcpv4OperationCodeEnum.BootRequest,
                TransactionId = 0x1234ABCD,
                HardwareAddressLength = 6,
                ClientHardwareAddress = clientHardwareAddress,
                // A real, freshly-booted client would set the broadcast flag instead of a real
                // ciaddr, since it has no address yet -- setting ciaddr here to this test's own
                // loopback client socket is a deliberate simplification that forces a
                // deterministic unicast reply (RFC 2131 Section 4.1's "client already has an
                // address" case), rather than depending on this sandboxed environment's own,
                // possibly restricted, UDP broadcast delivery behavior.
                ClientIpAddress = IPAddress.Loopback,
            };
            discover.SetMessageType(Dhcpv4MessageTypeEnum.Discover);
            discover.SetParameterRequestList([1, 3, 6]);

            byte[] discoverBytes = Dhcpv4MessageWriter.Write(discover);
            await client.SendAsync(
                discoverBytes,
                discoverBytes.Length,
                new IPEndPoint(IPAddress.Loopback, serverPort)
            );

            using CancellationTokenSource receiveTimeout = new(TimeSpan.FromSeconds(5));
            UdpReceiveResult received = await client.ReceiveAsync(receiveTimeout.Token);

            Assert.True(
                Dhcpv4MessageReader.TryRead(
                    received.Buffer,
                    out Dhcpv4Message offer,
                    out string failureReason
                ),
                failureReason
            );
            Assert.True(offer.TryGetMessageType(out Dhcpv4MessageTypeEnum messageType));
            Assert.Equal(Dhcpv4MessageTypeEnum.Offer, messageType);
            Assert.Equal(discover.TransactionId, offer.TransactionId);
            Assert.Equal(StubDhcpv4RequestHandler.OfferedAddress, offer.YourIpAddress);
            Assert.True(offer.TryGetServerIdentifier(out IPAddress serverIdentifier));
            Assert.Equal(StubDhcpv4RequestHandler.ServerIdentifier, serverIdentifier);
            Assert.True(handler.ReceivedRequest);
            Assert.Equal(clientHardwareAddress, handler.LastRequestHardwareAddress);
        }
        finally
        {
            await listener.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    ///     This test proves <c>Dhcpv4UdpListener.SendReplyAsync</c>'s real, production
    ///     reply-destination logic honors RFC 2131 Section 4.1's own stated priority order: a
    ///     request carrying a non-zero relay-agent address (<c>giaddr</c>) always gets its reply
    ///     unicast back to that relay agent, even when the same request's own broadcast flag is
    ///     also set -- proving <c>giaddr</c> genuinely takes priority, not merely that it works
    ///     when there is no broadcast flag to compete with.
    ///     <para>
    ///         The "relay agent" itself is a second, real socket, bound to its own specific
    ///         loopback address (<c>127.0.0.7</c>) on the exact same server port the listener
    ///         under test is bound to via a wildcard (<c>INADDR_ANY</c>) bind -- both sockets set
    ///         <c>SO_REUSEADDR</c> (the listener's own real production bind already does; see
    ///         <see cref="Dhcpv4UdpListener.StartAsync" />'s own doc comment), which lets a
    ///         specific-address bind coexist with an already-established wildcard bind on the same
    ///         port, with the operating system delivering any datagram addressed to that specific
    ///         address to the more specific socket. If the reply were ever (incorrectly)
    ///         broadcast instead, this "relay" socket would never receive anything and this test
    ///         would time out -- a real, meaningfully-discriminating proof, not a tautology.
    ///     </para>
    /// </summary>
    [Fact]
    public async Task RelayedDiscover_RepliesAreUnicastToTheRelayAgent_NotBroadcast()
    {
        int serverPort = GetFreeUdpPort();
        int clientPort = GetFreeUdpPort();
        IPAddress relayAgentAddress = IPAddress.Parse("127.0.0.7");

        StubDhcpv4RequestHandler handler = new();
        Dhcpv4ProtocolOptions options = new()
        {
            Enable = true,
            ServerPort = serverPort,
            ClientPort = clientPort,
        };
        Dhcpv4UdpListener listener = new(
            handler,
            Options.Create(options),
            NullLogger<Dhcpv4UdpListener>.Instance
        );

        await listener.StartAsync(CancellationToken.None);

        using Socket relaySocket = new(
            AddressFamily.InterNetwork,
            SocketType.Dgram,
            ProtocolType.Udp
        );
        relaySocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        relaySocket.Bind(new IPEndPoint(relayAgentAddress, serverPort));

        try
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);

            while (!listener.IsListening && DateTime.UtcNow < deadline)
                await Task.Delay(10);

            Assert.True(listener.IsListening, "The listener never finished binding its socket.");

            using UdpClient client = new(new IPEndPoint(IPAddress.Loopback, 0));

            Dhcpv4Message discover = new()
            {
                OperationCode = Dhcpv4OperationCodeEnum.BootRequest,
                TransactionId = 0x99AA88BB,
                HardwareAddressLength = 6,
                ClientHardwareAddress = [0x02, 0x00, 0x00, 0x00, 0x00, 0x02],
                RelayAgentIpAddress = relayAgentAddress,
                // Deliberately set alongside a non-zero giaddr -- proves the priority order this
                // test's own doc comment describes, rather than only the uncontested case.
                BroadcastFlagSet = true,
            };
            discover.SetMessageType(Dhcpv4MessageTypeEnum.Discover);

            byte[] discoverBytes = Dhcpv4MessageWriter.Write(discover);
            await client.SendAsync(
                discoverBytes,
                discoverBytes.Length,
                new IPEndPoint(IPAddress.Loopback, serverPort)
            );

            using CancellationTokenSource receiveTimeout = new(TimeSpan.FromSeconds(5));
            byte[] receiveBuffer = new byte[2048];
            EndPoint anyRemote = new IPEndPoint(IPAddress.Any, 0);
            SocketReceiveFromResult received = await relaySocket.ReceiveFromAsync(
                receiveBuffer,
                anyRemote,
                receiveTimeout.Token
            );

            Assert.True(
                Dhcpv4MessageReader.TryRead(
                    receiveBuffer[..received.ReceivedBytes],
                    out Dhcpv4Message offer,
                    out string failureReason
                ),
                failureReason
            );
            Assert.True(offer.TryGetMessageType(out Dhcpv4MessageTypeEnum messageType));
            Assert.Equal(Dhcpv4MessageTypeEnum.Offer, messageType);
            Assert.Equal(discover.TransactionId, offer.TransactionId);
        }
        finally
        {
            await listener.StopAsync(CancellationToken.None);
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
