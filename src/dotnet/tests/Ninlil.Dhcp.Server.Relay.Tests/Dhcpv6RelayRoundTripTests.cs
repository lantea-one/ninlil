using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Protocol.Dhcpv6;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Relay.Tests;

/// <summary>
///     This test proves the real <see cref="Dhcpv6RelayAgent" /> and the real
///     <see cref="Dhcpv6UdpListener" /> genuinely work together over real UDP sockets, both
///     directions: a real "client" socket's own SOLICIT genuinely reaches a fake "upstream server"
///     socket wrapped in a correctly-built RELAY-FORW envelope, and that fake upstream's own
///     crafted RELAY-REPL genuinely gets unwrapped and relayed back down, arriving at that same
///     "client" socket as an ordinary ADVERTISE -- the DHCPv6 analogue of
///     <see cref="Dhcpv4RelayRoundTripTests" />. This test also exercises
///     <see cref="Dhcpv6RelayAgent" />'s own background receive loop on its dedicated outbound
///     socket (see that class's own doc comment for why it needs one, unlike
///     <see cref="Dhcpv4RelayAgent" />) -- the genuinely different "receiving side" role (this
///     server accepting a RELAY-FORW an external relay agent forwarded to it) is proven
///     separately, in <c>Ninlil.Dhcp.Server.Core.Tests.Dhcpv6RelayReceivingRoundTripTests</c>.
/// </summary>
public sealed class Dhcpv6RelayRoundTripTests
{
    private static readonly IPAddress RelayLinkAddress = IPAddress.Parse("2001:db8:1::1");

    [Fact]
    public async Task Solicit_IsForwardedUpstreamWrappedInRelayForward_AndTheUpstreamsReplyIsRelayedBackToTheClient()
    {
        int relayListenPort = GetFreeUdpPort();
        int relayClientPort = GetFreeUdpPort();
        int upstreamPort = GetFreeUdpPort();

        Dhcpv6RelayOptions relayOptions = new()
        {
            Enable = true,
            UpstreamServerAddresses = ["::1"],
            UpstreamServerPort = upstreamPort,
        };
        Dhcpv6ProtocolOptions protocolOptions = new()
        {
            Enable = true,
            ServerPort = relayListenPort,
            ClientPort = relayClientPort,
        };

        Dhcpv6RelayAgent agent = new(
            Options.Create(relayOptions),
            Options.Create(protocolOptions),
            new FixedDhcpv6RelayAgentAddressProvider(RelayLinkAddress),
            NullLogger<Dhcpv6RelayAgent>.Instance
        );
        Dhcpv6UdpListener listener = new(
            agent,
            Options.Create(protocolOptions),
            NullLogger<Dhcpv6UdpListener>.Instance
        );

        // Both this relay's own dedicated outbound socket (its own background receive loop -- see
        // this class's own doc comment) and the ordinary shared listener need to be running for
        // this full, both-directions round trip to work.
        await agent.StartAsync(CancellationToken.None);
        await listener.StartAsync(CancellationToken.None);

        using UdpClient upstreamSocket = new(AddressFamily.InterNetworkV6);
        upstreamSocket.Client.Bind(new IPEndPoint(IPAddress.IPv6Loopback, upstreamPort));
        using UdpClient clientSocket = new(AddressFamily.InterNetworkV6);
        clientSocket.Client.Bind(new IPEndPoint(IPAddress.IPv6Loopback, relayClientPort));

        try
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);

            while (!listener.IsListening && DateTime.UtcNow < deadline)
                await Task.Delay(10);

            Assert.True(listener.IsListening, "The relay's own listener never finished binding.");

            byte[] clientDuid = [0x00, 0x01, 0x00, 0x01, 0x10, 0x20, 0x30, 0x40, 0x50, 0x60];

            // Step 1: the client's own SOLICIT "reaches the relay" (sent directly, to this relay's
            // own listening port, standing in for the relay having genuinely received a real
            // multicast SOLICIT on its own client-facing link).
            Dhcpv6Message solicit = new()
            {
                MessageType = Dhcpv6MessageTypeEnum.Solicit,
                TransactionId = 0x445566,
            };
            solicit.SetClientIdentifier(clientDuid);
            solicit.SetElapsedTime(TimeSpan.Zero);
            solicit.AddIdentityAssociationForNonTemporaryAddresses(
                new Dhcpv6IdentityAssociationForNonTemporaryAddresses { IdentityAssociationId = 1 }
            );

            byte[] solicitBytes = Dhcpv6MessageWriter.Write(solicit);
            await clientSocket.SendAsync(
                solicitBytes,
                solicitBytes.Length,
                new IPEndPoint(IPAddress.IPv6Loopback, relayListenPort)
            );

            // Step 2: the fake upstream server genuinely receives the forwarded message, wrapped
            // in a RELAY-FORW envelope this relay's own address/hop-count are correctly set on.
            using CancellationTokenSource upstreamReceiveTimeout = new(TimeSpan.FromSeconds(5));
            UdpReceiveResult forwardedResult = await upstreamSocket.ReceiveAsync(
                upstreamReceiveTimeout.Token
            );

            Assert.True(
                Dhcpv6RelayMessageReader.TryRead(
                    forwardedResult.Buffer,
                    out Dhcpv6RelayMessage relayForward,
                    out string relayForwardFailureReason
                ),
                relayForwardFailureReason
            );
            Assert.Equal(Dhcpv6MessageTypeEnum.RelayForward, relayForward.MessageType);
            Assert.Equal((byte)0, relayForward.HopCount);
            Assert.Equal(RelayLinkAddress, relayForward.LinkAddress);
            Assert.Equal(IPAddress.IPv6Loopback, relayForward.PeerAddress);

            Assert.True(relayForward.TryGetRelayMessagePayload(out byte[] innerSolicitBytes));
            Assert.True(
                Dhcpv6MessageReader.TryRead(
                    innerSolicitBytes,
                    out Dhcpv6Message forwardedSolicit,
                    out string innerFailureReason
                ),
                innerFailureReason
            );
            Assert.Equal(Dhcpv6MessageTypeEnum.Solicit, forwardedSolicit.MessageType);
            Assert.Equal(solicit.TransactionId, forwardedSolicit.TransactionId);

            // Step 3: the fake upstream server crafts an ADVERTISE, wraps it in a RELAY-REPL
            // envelope copying hop-count/link-address/peer-address unchanged (RFC 8415 Section
            // 16), and sends it back to wherever the RELAY-FORW actually came from -- this relay's
            // own dedicated outbound socket, at its own ephemeral local port.
            Dhcpv6Message advertise = new()
            {
                MessageType = Dhcpv6MessageTypeEnum.Advertise,
                TransactionId = forwardedSolicit.TransactionId,
            };
            advertise.SetServerIdentifier([0x00, 0x02, 0x00, 0x00, 0xAB, 0xCD]);
            Dhcpv6IdentityAssociationForNonTemporaryAddresses offeredNa = new()
            {
                IdentityAssociationId = 1,
            };
            offeredNa.Addresses.Add(
                new Dhcpv6IdentityAssociationAddress
                {
                    Address = IPAddress.Parse("2001:db8:1::100"),
                    PreferredLifetime = TimeSpan.FromHours(1),
                    ValidLifetime = TimeSpan.FromHours(2),
                }
            );
            advertise.AddIdentityAssociationForNonTemporaryAddresses(offeredNa);

            Dhcpv6RelayMessage relayReply = new()
            {
                MessageType = Dhcpv6MessageTypeEnum.RelayReply,
                HopCount = relayForward.HopCount,
                LinkAddress = relayForward.LinkAddress,
                PeerAddress = relayForward.PeerAddress,
            };
            relayReply.SetRelayMessagePayload(Dhcpv6MessageWriter.Write(advertise));

            byte[] relayReplyBytes = Dhcpv6RelayMessageWriter.Write(relayReply);
            await upstreamSocket.SendAsync(
                relayReplyBytes,
                relayReplyBytes.Length,
                forwardedResult.RemoteEndPoint
            );

            // Step 4: this relay's own dedicated outbound socket (its own background receive loop)
            // receives that RELAY-REPL, unwraps it, and relays the inner ADVERTISE on down to the
            // original client.
            using CancellationTokenSource clientReceiveTimeout = new(TimeSpan.FromSeconds(5));
            UdpReceiveResult relayedResult = await clientSocket.ReceiveAsync(
                clientReceiveTimeout.Token
            );

            Assert.True(
                Dhcpv6MessageReader.TryRead(
                    relayedResult.Buffer,
                    out Dhcpv6Message relayedAdvertise,
                    out string relayedFailureReason
                ),
                relayedFailureReason
            );
            Assert.Equal(Dhcpv6MessageTypeEnum.Advertise, relayedAdvertise.MessageType);
            Assert.Equal(solicit.TransactionId, relayedAdvertise.TransactionId);
            Assert.True(
                relayedAdvertise.TryGetIdentityAssociationsForNonTemporaryAddresses(
                    out IReadOnlyList<Dhcpv6IdentityAssociationForNonTemporaryAddresses> relayedOffered
                )
            );
            Assert.Single(relayedOffered);
            Assert.Equal(
                IPAddress.Parse("2001:db8:1::100"),
                relayedOffered[0].Addresses[0].Address
            );
        }
        finally
        {
            await listener.StopAsync(CancellationToken.None);
            await agent.StopAsync(CancellationToken.None);
            agent.Dispose();
        }
    }

    /// <summary>
    ///     This method obtains a currently-unused, unprivileged IPv6 UDP port by asking the
    ///     operating system to assign an ephemeral one, reading it back, and immediately releasing
    ///     it -- the same convention <c>Dhcpv4RelayRoundTripTests.GetFreeUdpPort</c> establishes,
    ///     adapted to IPv6.
    /// </summary>
    private static int GetFreeUdpPort()
    {
        using Socket probe = new(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
        probe.Bind(new IPEndPoint(IPAddress.IPv6Loopback, 0));
        return ((IPEndPoint)probe.LocalEndPoint).Port;
    }
}
