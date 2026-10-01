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
///     This class tests <see cref="Dhcpv4RelayAgent" /> behind a running
///     <see cref="Dhcpv4UdpListener" />, in both directions, over loopback UDP sockets. A client
///     socket's DISCOVER must reach a fake upstream server with <c>hops</c> and <c>giaddr</c> set,
///     and the OFFER the fake server sends back must reach the client socket.
///     <para>
///         The test uses unprivileged loopback ports instead of 67 and 68. The relay logic does
///         not depend on the port numbers.
///     </para>
///     <para>
///         The OFFER sets <c>ciaddr</c> and clears the broadcast flag, unlike a typical OFFER, so
///         the relay unicasts it. This avoids depending on broadcast delivery in the test
///         environment, as <c>Ninlil.Dhcp.Protocol.Dhcpv4.Tests.Dhcpv4UdpRoundTripTests</c> does.
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

            // Step 1: the client's DISCOVER reaches the relay. It is sent straight to the
            // listener's port in place of a subnet broadcast.
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

            // Step 2: the fake upstream server receives the forwarded request with hops and
            // giaddr set.
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

            // Step 3: the fake upstream server sends an OFFER to giaddr at the server port, where
            // RFC 2131 Section 4.1 sends replies through a relay and where the listener is bound.
            Dhcpv4Message offer = new()
            {
                OperationCode = Dhcpv4OperationCodeEnum.BootReply,
                TransactionId = forwarded.TransactionId,
                YourIpAddress = IPAddress.Parse("203.0.113.77"),
                RelayAgentIpAddress = forwarded.RelayAgentIpAddress,
                ClientHardwareAddress = forwarded.ClientHardwareAddress,
                HardwareAddressLength = forwarded.HardwareAddressLength,
                // Forces unicast delivery (see the class summary).
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

            // Step 4: the relay receives the BootReply on the same listener socket and relays it
            // to the client.
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
    ///     This method returns a free UDP port by binding to port 0, reading the assigned port and
    ///     releasing it. Another process could take the port before the test binds it; the risk
    ///     is accepted.
    /// </summary>
    private static int GetFreeUdpPort()
    {
        using UdpClient probe = new(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint).Port;
    }
}
