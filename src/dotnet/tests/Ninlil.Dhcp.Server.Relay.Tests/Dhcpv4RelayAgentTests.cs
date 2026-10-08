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
///     This class tests the forwarding side of <see cref="Dhcpv4RelayAgent" />: <c>giaddr</c> and
///     hops handling for first and chained hops, Relay Agent Information, the hop limit and an
///     empty upstream list. Each test calls <see cref="Dhcpv4RelayAgent.HandleRequestAsync" />
///     directly and reads what arrives on a loopback UDP socket acting as the upstream server.
///     The full path through <see cref="Dhcpv4UdpListener" /> is covered by
///     <see cref="Dhcpv4RelayRoundTripTests" />.
/// </summary>
public sealed class Dhcpv4RelayAgentTests
{
    [Fact]
    public async Task ChainedRequest_WithAnAlreadySetGiaddr_PreservesItButStillIncrementsHops()
    {
        int upstreamPort = GetFreeUdpPort();
        IPAddress thisRelayAddress = IPAddress.Parse("127.0.0.11");
        IPAddress upstreamRelayAddress = IPAddress.Parse("127.0.0.12");

        using UdpClient upstreamSocket = new(new IPEndPoint(IPAddress.Loopback, upstreamPort));
        using Dhcpv4RelayAgent agent = CreateAgent(
            thisRelayAddress,
            upstreamServerAddresses: ["127.0.0.1"],
            upstreamPort
        );

        Dhcpv4Message request = new()
        {
            OperationCode = Dhcpv4OperationCodeEnum.BootRequest,
            TransactionId = 0x1111_2222,
            HardwareAddressLength = 6,
            ClientHardwareAddress = [0x02, 0x00, 0x00, 0x00, 0x00, 0x03],
            // Already forwarded by another relay: giaddr set, hops at 1.
            RelayAgentIpAddress = upstreamRelayAddress,
            Hops = 1,
        };
        request.SetMessageType(Dhcpv4MessageTypeEnum.Discover);

        await agent.HandleRequestAsync(
            request,
            new IPEndPoint(IPAddress.Loopback, 0),
            CancellationToken.None
        );

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        UdpReceiveResult received = await upstreamSocket.ReceiveAsync(timeout.Token);

        Assert.True(
            Dhcpv4MessageReader.TryRead(
                received.Buffer,
                out Dhcpv4Message forwarded,
                out string reason
            ),
            reason
        );

        // RFC 2131 Section 2: only "the first" relay agent sets giaddr, so it is unchanged.
        Assert.Equal(upstreamRelayAddress, forwarded.RelayAgentIpAddress);
        // Every relay increments hops, first or not.
        Assert.Equal((byte)2, forwarded.Hops);
    }

    [Fact]
    public async Task FirstHopRequest_SetsGiaddrToThisRelaysOwnAddress_AndAddsOption82WhenConfigured()
    {
        int upstreamPort = GetFreeUdpPort();
        IPAddress thisRelayAddress = IPAddress.Parse("127.0.0.13");

        using UdpClient upstreamSocket = new(new IPEndPoint(IPAddress.Loopback, upstreamPort));
        using Dhcpv4RelayAgent agent = CreateAgent(
            thisRelayAddress,
            upstreamServerAddresses: ["127.0.0.1"],
            upstreamPort,
            circuitId: "circuit-42",
            remoteId: "remote-99"
        );

        Dhcpv4Message request = new()
        {
            OperationCode = Dhcpv4OperationCodeEnum.BootRequest,
            TransactionId = 0x3333_4444,
            HardwareAddressLength = 6,
            ClientHardwareAddress = [0x02, 0x00, 0x00, 0x00, 0x00, 0x04],
        };
        request.SetMessageType(Dhcpv4MessageTypeEnum.Discover);

        await agent.HandleRequestAsync(
            request,
            new IPEndPoint(IPAddress.Loopback, 0),
            CancellationToken.None
        );

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        UdpReceiveResult received = await upstreamSocket.ReceiveAsync(timeout.Token);

        Assert.True(
            Dhcpv4MessageReader.TryRead(
                received.Buffer,
                out Dhcpv4Message forwarded,
                out string reason
            ),
            reason
        );
        Assert.Equal(thisRelayAddress, forwarded.RelayAgentIpAddress);
        Assert.Equal((byte)1, forwarded.Hops);
        Assert.True(forwarded.TryGetRelayAgentCircuitId(out byte[] circuitId));
        Assert.Equal("circuit-42", System.Text.Encoding.ASCII.GetString(circuitId));
        Assert.True(forwarded.TryGetRelayAgentRemoteId(out byte[] remoteId));
        Assert.Equal("remote-99", System.Text.Encoding.ASCII.GetString(remoteId));
    }

    [Fact]
    public async Task RequestAtTheHopLimit_IsDroppedRatherThanForwarded()
    {
        int upstreamPort = GetFreeUdpPort();

        using UdpClient upstreamSocket = new(new IPEndPoint(IPAddress.Loopback, upstreamPort));
        using Dhcpv4RelayAgent agent = CreateAgent(
            IPAddress.Parse("127.0.0.14"),
            upstreamServerAddresses: ["127.0.0.1"],
            upstreamPort
        );

        Dhcpv4Message request = new()
        {
            OperationCode = Dhcpv4OperationCodeEnum.BootRequest,
            TransactionId = 0x5555_6666,
            HardwareAddressLength = 6,
            ClientHardwareAddress = [0x02, 0x00, 0x00, 0x00, 0x00, 0x05],
            // Dhcpv4RelayAgent.MaxHops.
            Hops = 16,
        };
        request.SetMessageType(Dhcpv4MessageTypeEnum.Discover);

        await agent.HandleRequestAsync(
            request,
            new IPEndPoint(IPAddress.Loopback, 0),
            CancellationToken.None
        );

        using CancellationTokenSource timeout = new(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await upstreamSocket.ReceiveAsync(timeout.Token)
        );
    }

    [Fact]
    public async Task RequestWithNoUpstreamServersConfigured_IsDroppedRatherThanThrowing()
    {
        using Dhcpv4RelayAgent agent = CreateAgent(
            IPAddress.Parse("127.0.0.15"),
            upstreamServerAddresses: [],
            upstreamServerPort: 67
        );

        Dhcpv4Message request = new()
        {
            OperationCode = Dhcpv4OperationCodeEnum.BootRequest,
            TransactionId = 0x7777_8888,
            HardwareAddressLength = 6,
            ClientHardwareAddress = [0x02, 0x00, 0x00, 0x00, 0x00, 0x06],
        };
        request.SetMessageType(Dhcpv4MessageTypeEnum.Discover);

        // Passes if nothing throws; the request is dropped with a warning.
        await agent.HandleRequestAsync(
            request,
            new IPEndPoint(IPAddress.Loopback, 0),
            CancellationToken.None
        );
    }

    [Fact]
    public async Task Reply_FromAnAddressThatIsNotAnUpstreamServer_IsNotRelayedToTheClient()
    {
        int clientPort = GetFreeUdpPort();
        IPAddress relayAddress = IPAddress.Parse("127.0.0.16");

        using UdpClient clientSocket = new(new IPEndPoint(IPAddress.Loopback, clientPort));
        using Dhcpv4RelayAgent agent = CreateAgent(
            relayAddress,
            upstreamServerAddresses: ["127.0.0.1"],
            upstreamServerPort: 67,
            clientPort: clientPort
        );

        // The right giaddr, but sent by a host that is not a configured upstream server.
        await agent.HandleRequestAsync(
            CreateOffer(relayAddress),
            new IPEndPoint(IPAddress.Parse("192.0.2.99"), 67),
            CancellationToken.None
        );

        using CancellationTokenSource timeout = new(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await clientSocket.ReceiveAsync(timeout.Token)
        );
    }

    [Fact]
    public async Task Reply_FromAConfiguredUpstreamServer_IsRelayedToTheClient()
    {
        int clientPort = GetFreeUdpPort();
        IPAddress relayAddress = IPAddress.Parse("127.0.0.17");

        using UdpClient clientSocket = new(new IPEndPoint(IPAddress.Loopback, clientPort));
        using Dhcpv4RelayAgent agent = CreateAgent(
            relayAddress,
            upstreamServerAddresses: ["127.0.0.1"],
            upstreamServerPort: 67,
            clientPort: clientPort
        );

        // An IPv4-mapped source (a dual-stack listener socket) matches the IPv4 entry.
        await agent.HandleRequestAsync(
            CreateOffer(relayAddress),
            new IPEndPoint(IPAddress.Loopback.MapToIPv6(), 67),
            CancellationToken.None
        );

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        UdpReceiveResult received = await clientSocket.ReceiveAsync(timeout.Token);

        Assert.True(
            Dhcpv4MessageReader.TryRead(
                received.Buffer,
                out Dhcpv4Message relayed,
                out string reason
            ),
            reason
        );
        Assert.Equal(IPAddress.Parse("203.0.113.78"), relayed.YourIpAddress);
    }

    /// <summary>
    ///     This method builds an OFFER carrying the given <c>giaddr</c> that the relay delivers by
    ///     unicast to the loopback address (through <c>ciaddr</c>).
    /// </summary>
    private static Dhcpv4Message CreateOffer(IPAddress giaddr)
    {
        Dhcpv4Message offer = new()
        {
            OperationCode = Dhcpv4OperationCodeEnum.BootReply,
            TransactionId = 0x9999_0001,
            HardwareAddressLength = 6,
            ClientHardwareAddress = [0x02, 0x00, 0x00, 0x00, 0x00, 0x07],
            YourIpAddress = IPAddress.Parse("203.0.113.78"),
            RelayAgentIpAddress = giaddr,
            ClientIpAddress = IPAddress.Loopback,
        };
        offer.SetMessageType(Dhcpv4MessageTypeEnum.Offer);
        return offer;
    }

    /// <summary>
    ///     This method builds a <see cref="Dhcpv4RelayAgent" /> by hand, without dependency
    ///     injection, with a fixed relay address and the given upstream servers.
    /// </summary>
    private static Dhcpv4RelayAgent CreateAgent(
        IPAddress relayAddress,
        IReadOnlyList<string> upstreamServerAddresses,
        int upstreamServerPort,
        string circuitId = null,
        string remoteId = null,
        int clientPort = 68
    ) =>
        new(
            Options.Create(
                new Dhcpv4RelayOptions
                {
                    Enable = true,
                    UpstreamServerAddresses = [.. upstreamServerAddresses],
                    UpstreamServerPort = upstreamServerPort,
                    RelayAgentCircuitId = circuitId,
                    RelayAgentRemoteId = remoteId,
                }
            ),
            Options.Create(new Dhcpv4ProtocolOptions { ClientPort = clientPort }),
            new FixedDhcpv4RelayAgentAddressProvider(relayAddress),
            NullLogger<Dhcpv4RelayAgent>.Instance
        );

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
