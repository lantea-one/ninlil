using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Dhcpv4.Tests;

/// <summary>
///     This test class checks the DHCPv4 UDP transport and codec together over loopback sockets
///     (the in-memory codec tests are in <c>Ninlil.Dhcp.Common.Tests</c>): a
///     <see cref="Dhcpv4UdpListener" /> binds a UDP socket, a <see cref="UdpClient" /> sends an
///     encoded DISCOVER, <see cref="StubDhcpv4RequestHandler" /> answers with a fixed OFFER, and
///     the test decodes the reply. It also checks that a relayed request's reply goes to the
///     relay agent.
///     <para>
///         The tests use unprivileged high ports chosen at run time instead of 67 and 68, which
///         need root to bind. The socket handling and wire format do not depend on the port
///         number.
///     </para>
/// </summary>
public class Dhcpv4UdpRoundTripTests
{
    [Fact]
    public async Task DiscoverThenOffer_RoundTripsOverARealSocket()
    {
        // The client socket binds first, on an OS-assigned port, because the listener sends its
        // replies to the configured client port. The server binds port 0 and reports its port.
        using UdpClient client = new(new IPEndPoint(IPAddress.Loopback, 0));
        int clientPort = ((IPEndPoint)client.Client.LocalEndPoint).Port;

        StubDhcpv4RequestHandler handler = new();
        Dhcpv4ProtocolOptions options = new()
        {
            Enable = true,
            ServerPort = 0,
            ClientPort = clientPort,
        };
        Dhcpv4UdpListener listener = new(
            handler,
            Options.Create(options),
            NullLogger<Dhcpv4UdpListener>.Instance
        );

        await listener.StartAsync(CancellationToken.None);
        int serverPort = listener.BoundPort;

        try
        {
            // StartAsync binds before it returns, so IsListening is already true; the poll only
            // guards against that changing.
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);

            while (!listener.IsListening && DateTime.UtcNow < deadline)
                await Task.Delay(10);

            Assert.True(listener.IsListening, "The listener never finished binding its socket.");

            byte[] clientHardwareAddress = [0x02, 0x00, 0x00, 0x00, 0x00, 0x01];

            Dhcpv4Message discover = new()
            {
                OperationCode = Dhcpv4OperationCodeEnum.BootRequest,
                TransactionId = 0x1234ABCD,
                HardwareAddressLength = 6,
                ClientHardwareAddress = clientHardwareAddress,
                // A client without an address would not set ciaddr. Setting it to the loopback
                // address forces a unicast reply (RFC 2131 Section 4.1's "client has an address"
                // case), so the test does not depend on broadcast delivery in the test
                // environment.
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
    ///     This test checks the RFC 2131 Section 4.1 priority in
    ///     <c>Dhcpv4UdpListener.SendReplyAsync</c>: a request with a non-zero relay-agent address
    ///     (<c>giaddr</c>) gets its reply unicast to that relay agent even when its broadcast flag
    ///     is also set.
    ///     <para>
    ///         The "relay agent" is a second socket bound to <c>127.0.0.7</c> on the listener's
    ///         server port, while the listener has a wildcard bind on that port. Both set
    ///         <c>SO_REUSEADDR</c> (see <see cref="Dhcpv4UdpListener.StartAsync" />), so the
    ///         specific bind can coexist with the wildcard one and receives datagrams addressed to
    ///         <c>127.0.0.7</c>. If the reply were broadcast instead, the relay socket would get
    ///         nothing and the test would time out.
    ///     </para>
    /// </summary>
    [Fact]
    public async Task RelayedDiscover_RepliesAreUnicastToTheRelayAgent_NotBroadcast()
    {
        IPAddress relayAgentAddress = IPAddress.Parse("127.0.0.7");

        StubDhcpv4RequestHandler handler = new();
        Dhcpv4ProtocolOptions options = new()
        {
            Enable = true,
            ServerPort = 0,
            // A relayed reply goes to the relay agent on the server port, so nothing is ever sent
            // to the client port and none is bound.
            ClientPort = 0,
        };
        Dhcpv4UdpListener listener = new(
            handler,
            Options.Create(options),
            NullLogger<Dhcpv4UdpListener>.Instance
        );

        await listener.StartAsync(CancellationToken.None);
        int serverPort = listener.BoundPort;

        // A relayed reply goes to the relay agent on the configured server port, so the options
        // must name the port the listener got instead of 0. The listener reads it on every send.
        options.ServerPort = serverPort;

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
                // Set together with a non-zero giaddr to test the priority, not just the case
                // with no competing flag.
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
}
