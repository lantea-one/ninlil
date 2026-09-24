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
///     This test proves <see cref="Dhcpv4RelayAgent" />'s own RFC 2131 Section 4.1/RFC 3046
///     behavior directly, by invoking its real <see cref="Dhcpv4RelayAgent.HandleRequestAsync" />
///     method against real UDP sockets standing in for an upstream DHCPv4 server and the
///     originating client -- edge/branch coverage this project's own separate, real, full
///     <see cref="Dhcpv4UdpListener" /> round trip (<see cref="Dhcpv4RelayRoundTripTests" />)
///     deliberately does not repeat.
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
            // A message a different, upstream-of-us relay has already touched: giaddr already
            // set, hops already at 1.
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

        // RFC 2131 Section 2: giaddr is filled in only by "the first" relay agent -- this relay is
        // not the first one here, so the already-set value must survive unchanged.
        Assert.Equal(upstreamRelayAddress, forwarded.RelayAgentIpAddress);
        // Every relay along the path still increments hops, regardless of whether it is the first.
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
            // This project's own defensive loop-prevention limit (Dhcpv4RelayAgent.MaxHops).
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

        // Simply must not throw -- there is nowhere sensible to forward this to.
        await agent.HandleRequestAsync(
            request,
            new IPEndPoint(IPAddress.Loopback, 0),
            CancellationToken.None
        );
    }

    /// <summary>
    ///     This method builds a real <see cref="Dhcpv4RelayAgent" /> against a fixed, deterministic
    ///     relay address and a caller-chosen upstream server list -- avoiding this test project's
    ///     own dependency injection registration entirely, since this class's own constructor
    ///     dependencies are cheap enough to construct directly by hand.
    /// </summary>
    private static Dhcpv4RelayAgent CreateAgent(
        IPAddress relayAddress,
        IReadOnlyList<string> upstreamServerAddresses,
        int upstreamServerPort,
        string circuitId = null,
        string remoteId = null
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
            Options.Create(new Dhcpv4ProtocolOptions()),
            new FixedDhcpv4RelayAgentAddressProvider(relayAddress),
            NullLogger<Dhcpv4RelayAgent>.Instance
        );

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
