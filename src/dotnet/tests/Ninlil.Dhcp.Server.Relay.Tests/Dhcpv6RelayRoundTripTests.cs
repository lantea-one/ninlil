using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Protocol.Dhcpv6;
using Ninlil.Dhcp.Server.Relay.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Relay.Tests;

/// <summary>
///     This class tests <see cref="Dhcpv6RelayAgent" /> behind a running
///     <see cref="Dhcpv6UdpListener" />, in both directions, over IPv6 loopback UDP sockets. A
///     client socket's SOLICIT must reach a fake upstream server inside a RELAY-FORW with the
///     expected hop-count, link-address and peer-address, and the RELAY-REPL the fake server sends
///     back must arrive at the client as a plain ADVERTISE. The reply leg runs through the relay's
///     receive loop on its outbound socket. It is the DHCPv6 counterpart of
///     <see cref="Dhcpv4RelayRoundTripTests" />. The server side of relaying (this server
///     answering a RELAY-FORW from another relay) is tested in
///     <c>Ninlil.Dhcp.Server.Core.Tests.Dhcpv6RelayReceivingRoundTripTests</c>.
/// </summary>
public sealed class Dhcpv6RelayRoundTripTests
{
    private static readonly IPAddress RelayLinkAddress = IPAddress.Parse("2001:db8:1::1");

    [Fact]
    public async Task Solicit_IsForwardedUpstreamWrappedInRelayForward_AndTheUpstreamsReplyIsRelayedBackToTheClient()
    {
        if (!LocalAddresses.CanBind(IPAddress.IPv6Loopback))
            return;

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

        // Start both the relay's receive loop and the listener; each carries one direction.
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

            // Step 1: the client's SOLICIT reaches the relay. It is sent straight to the listener's
            // port in place of a multicast.
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

            // Step 2: the fake upstream server receives the message inside a RELAY-FORW with the
            // relay's link-address and hop-count 0.
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

            // Step 3: the fake upstream server wraps an ADVERTISE in a RELAY-REPL, copying
            // hop-count, link-address and peer-address from the RELAY-FORW, and sends it to the
            // RELAY-FORW's source: the relay's outbound socket on its ephemeral port.
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

            // Step 4: the relay's receive loop gets the RELAY-REPL, unwraps it and sends the
            // ADVERTISE to the client.
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
    ///     This method returns a free IPv6 UDP port by binding to port 0 on loopback, reading the
    ///     assigned port and releasing it, as <c>Dhcpv4RelayRoundTripTests.GetFreeUdpPort</c> does
    ///     for IPv4.
    /// </summary>
    private static int GetFreeUdpPort()
    {
        using Socket probe = new(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
        probe.Bind(new IPEndPoint(IPAddress.IPv6Loopback, 0));
        return ((IPEndPoint)probe.LocalEndPoint).Port;
    }
}
