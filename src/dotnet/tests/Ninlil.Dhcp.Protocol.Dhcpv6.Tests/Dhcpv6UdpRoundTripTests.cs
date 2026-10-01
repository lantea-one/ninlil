using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Dhcpv6.Tests;

/// <summary>
///     This test class checks the DHCPv6 UDP transport and codec together over IPv6 loopback, like
///     <c>Ninlil.Dhcp.Protocol.Dhcpv4.Tests.Dhcpv4UdpRoundTripTests</c>: a
///     <see cref="Dhcpv6UdpListener" /> binds an IPv6 UDP socket, a <see cref="UdpClient" /> sends
///     an encoded SOLICIT, <see cref="StubDhcpv6RequestHandler" /> answers with a fixed ADVERTISE,
///     and the test decodes the reply.
///     <para>
///         It uses unprivileged high ports instead of 546 and 547, and sends by unicast to
///         loopback rather than to the multicast group, since multicast delivery cannot be relied
///         on in every test environment. The listener still joins the group on every start (a
///         failure on one interface is only logged). The wire format, socket handling and reply
///         destination do not depend on how the request was addressed.
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
    ///     This method gets an unused high IPv6 UDP port, as
    ///     <c>Dhcpv4UdpRoundTripTests.GetFreeUdpPort</c> does for IPv4.
    /// </summary>
    private static int GetFreeUdpPort()
    {
        using Socket probe = new(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
        probe.Bind(new IPEndPoint(IPAddress.IPv6Loopback, 0));
        return ((IPEndPoint)probe.LocalEndPoint).Port;
    }
}
