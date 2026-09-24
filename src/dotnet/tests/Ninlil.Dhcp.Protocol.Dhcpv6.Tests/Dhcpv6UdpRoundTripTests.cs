using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Dhcpv6.Tests;

/// <summary>
///     This test proves the DHCPv6 UDP transport and wire codec genuinely work together over a
///     real socket, not merely as an in-memory byte-array round trip (that layer is already
///     covered exhaustively, and far faster, by <c>Ninlil.Dhcp.Common.Tests</c>): a real
///     <see cref="Dhcpv6UdpListener" /> binds a real IPv6 UDP socket, a real
///     <see cref="UdpClient" /> sends a real, wire-encoded SOLICIT datagram to it over the IPv6
///     loopback address, a stub <see cref="StubDhcpv6RequestHandler" /> answers with a canned
///     ADVERTISE, and this test's own socket receives and decodes that reply back into a
///     <see cref="Dhcpv6Message" /> -- the DHCPv6 analogue of
///     <c>Ninlil.Dhcp.Protocol.Dhcpv4.Tests.Dhcpv4UdpRoundTripTests</c>.
///     <para>
///         This runs on ordinary, unprivileged, dynamically-chosen high ports rather than the
///         real IANA-assigned 546/547 DHCPv6 ports, since binding a port below 1024 needs root
///         privileges an automated test suite does not run with, and it sends directly over
///         unicast loopback rather than joining/sending to the real multicast group -- this
///         listener's own real multicast-group join is exercised for real every time it starts
///         (a failure on any one interface can never abort it), but a genuinely reliable, portable way
///         to prove multicast delivery itself works inside an arbitrary, possibly
///         multicast-restricted sandboxed test environment does not exist -- this test instead
///         proves the wire protocol, socket-handling, and reply-destination logic itself, which is
///         identical regardless of how the original datagram was addressed.
///     </para>
/// </summary>
public class Dhcpv6UdpRoundTripTests
{
    [Fact]
    public async Task SolicitThenAdvertise_RoundTripsOverARealSocket()
    {
        int serverPort = GetFreeUdpPort();
        int clientPort = GetFreeUdpPort();

        StubDhcpv6RequestHandler handler = new();
        Dhcpv6ProtocolOptions options = new()
        {
            Enable = true,
            ServerPort = serverPort,
            ClientPort = clientPort,
        };
        Dhcpv6UdpListener listener = new(
            handler,
            Options.Create(options),
            NullLogger<Dhcpv6UdpListener>.Instance
        );

        await listener.StartAsync(CancellationToken.None);

        try
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);

            while (!listener.IsListening && DateTime.UtcNow < deadline)
                await Task.Delay(10);

            Assert.True(listener.IsListening, "The listener never finished binding its socket.");

            using UdpClient client = new(AddressFamily.InterNetworkV6);
            client.Client.Bind(new IPEndPoint(IPAddress.IPv6Loopback, clientPort));

            byte[] clientDuid = [0x00, 0x01, 0x00, 0x01, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66];

            Dhcpv6Message solicit = new()
            {
                MessageType = Dhcpv6MessageTypeEnum.Solicit,
                TransactionId = 0xABCDEF,
            };
            solicit.SetClientIdentifier(clientDuid);
            solicit.SetElapsedTime(TimeSpan.Zero);

            byte[] solicitBytes = Dhcpv6MessageWriter.Write(solicit);
            await client.SendAsync(
                solicitBytes,
                solicitBytes.Length,
                new IPEndPoint(IPAddress.IPv6Loopback, serverPort)
            );

            using CancellationTokenSource receiveTimeout = new(TimeSpan.FromSeconds(5));
            UdpReceiveResult received = await client.ReceiveAsync(receiveTimeout.Token);

            Assert.True(
                Dhcpv6MessageReader.TryRead(
                    received.Buffer,
                    out Dhcpv6Message advertise,
                    out string failureReason
                ),
                failureReason
            );
            Assert.Equal(Dhcpv6MessageTypeEnum.Advertise, advertise.MessageType);
            Assert.Equal(solicit.TransactionId, advertise.TransactionId);
            Assert.True(advertise.TryGetServerIdentifier(out byte[] serverIdentifier));
            Assert.Equal(StubDhcpv6RequestHandler.ServerIdentifier, serverIdentifier);
            Assert.True(advertise.TryGetClientIdentifier(out byte[] echoedClientDuid));
            Assert.Equal(clientDuid, echoedClientDuid);

            Assert.True(
                advertise.TryGetIdentityAssociationsForNonTemporaryAddresses(
                    out IReadOnlyList<Dhcpv6IdentityAssociationForNonTemporaryAddresses> associations
                )
            );
            Assert.Single(associations);
            Assert.Single(associations[0].Addresses);
            Assert.Equal(
                StubDhcpv6RequestHandler.OfferedAddress,
                associations[0].Addresses[0].Address
            );

            Assert.True(handler.ReceivedRequest);
        }
        finally
        {
            await listener.StopAsync(CancellationToken.None);
        }
    }

    /// <summary>
    ///     This method obtains a currently-unused, unprivileged IPv6 UDP port by asking the
    ///     operating system to assign an ephemeral one, reading it back, and immediately releasing
    ///     it -- the same convention <c>Dhcpv4UdpRoundTripTests.GetFreeUdpPort</c> establishes,
    ///     adapted to IPv6.
    /// </summary>
    private static int GetFreeUdpPort()
    {
        using Socket probe = new(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
        probe.Bind(new IPEndPoint(IPAddress.IPv6Loopback, 0));
        return ((IPEndPoint)probe.LocalEndPoint).Port;
    }
}
