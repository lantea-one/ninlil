using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Protocol.Dhcpv6;
using Ninlil.Dhcp.Server.Relay.Tests.Fakes;
using Ninlil.Dns.Common.Net;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Relay.Tests;

/// <summary>
///     This class tests the drop paths of <see cref="Dhcpv6RelayAgent" />: no upstream servers, a
///     RELAY-FORW arriving at the relay, and a RELAY-REPL over the hop limit. Each test calls
///     <see cref="Dhcpv6RelayAgent.HandleRequestAsync" /> or
///     <see cref="Dhcpv6RelayAgent.HandleRelayMessageAsync" /> directly and checks that it returns
///     <see langword="null" /> without throwing. The full path is covered by
///     <see cref="Dhcpv6RelayRoundTripTests" />. The link-local tests also check the scope ID
///     the relay gives a client's address (a pure rule, and one live case on a real interface).
/// </summary>
public sealed class Dhcpv6RelayAgentTests
{
    [Fact]
    public async Task HandleRequestAsync_WithNoUpstreamServersConfigured_IsDroppedRatherThanThrowing()
    {
        using Dhcpv6RelayAgent agent = CreateAgent(upstreamServerAddresses: []);

        Dhcpv6Message request = new() { MessageType = Dhcpv6MessageTypeEnum.Solicit };
        request.SetClientIdentifier([0x00, 0x01, 0x00, 0x01, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06]);

        // Passes if nothing throws; the message is dropped with a warning.
        Dhcpv6Message reply = await agent.HandleRequestAsync(
            request,
            new IPEndPoint(IPAddress.IPv6Loopback, 0),
            CancellationToken.None
        );

        Assert.Null(reply);
    }

    [Fact]
    public async Task HandleRelayMessageAsync_ARelayForwardArrivingHere_IsIgnored()
    {
        using Dhcpv6RelayAgent agent = CreateAgent(upstreamServerAddresses: ["::1"]);

        // Only a RELAY-REPL answers this relay; chained relays are not supported, so a
        // RELAY-FORW is ignored.
        Dhcpv6Message innerMessage = new() { MessageType = Dhcpv6MessageTypeEnum.Solicit };
        innerMessage.SetClientIdentifier([
            0x00,
            0x01,
            0x00,
            0x01,
            0x0A,
            0x0B,
            0x0C,
            0x0D,
            0x0E,
            0x0F,
        ]);

        Dhcpv6RelayMessage relayForward = new()
        {
            MessageType = Dhcpv6MessageTypeEnum.RelayForward,
            LinkAddress = IPAddress.Parse("2001:db8:1::1"),
            PeerAddress = IPAddress.Parse("fe80::1"),
        };
        relayForward.SetRelayMessagePayload(Dhcpv6MessageWriter.Write(innerMessage));

        Dhcpv6RelayMessage reply = await agent.HandleRelayMessageAsync(
            relayForward,
            new IPEndPoint(IPAddress.IPv6Loopback, 0),
            CancellationToken.None
        );

        Assert.Null(reply);
    }

    [Fact]
    public async Task HandleRelayMessageAsync_HopCountAboveDefensiveLimit_IsDiscarded()
    {
        using Dhcpv6RelayAgent agent = CreateAgent(upstreamServerAddresses: ["::1"]);

        Dhcpv6Message innerReply = new() { MessageType = Dhcpv6MessageTypeEnum.Reply };
        innerReply.SetServerIdentifier([0x00, 0x02, 0x00, 0x00, 0xAB, 0xCD]);

        Dhcpv6RelayMessage relayReply = new()
        {
            MessageType = Dhcpv6MessageTypeEnum.RelayReply,
            // One above Dhcpv6RelayAgent.MaxHops.
            HopCount = 17,
            LinkAddress = IPAddress.Parse("2001:db8:1::1"),
            PeerAddress = IPAddress.Parse("fe80::1"),
        };
        relayReply.SetRelayMessagePayload(Dhcpv6MessageWriter.Write(innerReply));

        Dhcpv6RelayMessage reply = await agent.HandleRelayMessageAsync(
            relayReply,
            new IPEndPoint(IPAddress.IPv6Loopback, 0),
            CancellationToken.None
        );

        Assert.Null(reply);
    }

    [Fact]
    public async Task HandleRelayMessageAsync_ReplyFromANonUpstreamSource_IsNotRelayed()
    {
        await AssertRelayedAsync(
            Dhcpv6MessageTypeEnum.Reply,
            IPAddress.Parse("2001:db8::99"),
            linkAddress: IPAddress.Parse("2001:db8:1::1"),
            expectRelayed: false
        );
    }

    [Fact]
    public async Task HandleRelayMessageAsync_ReplyWithAWrongLinkAddress_IsNotRelayed()
    {
        await AssertRelayedAsync(
            Dhcpv6MessageTypeEnum.Reply,
            IPAddress.IPv6Loopback,
            linkAddress: IPAddress.Parse("2001:db8:2::1"),
            expectRelayed: false
        );
    }

    [Fact]
    public async Task HandleRelayMessageAsync_ReplyWrappingASolicit_IsNotRelayed()
    {
        await AssertRelayedAsync(
            Dhcpv6MessageTypeEnum.Solicit,
            IPAddress.IPv6Loopback,
            linkAddress: IPAddress.Parse("2001:db8:1::1"),
            expectRelayed: false
        );
    }

    [Theory]
    [InlineData(Dhcpv6MessageTypeEnum.Advertise)]
    [InlineData(Dhcpv6MessageTypeEnum.Reply)]
    [InlineData(Dhcpv6MessageTypeEnum.Reconfigure)]
    public async Task HandleRelayMessageAsync_AValidReplyFromAnUpstreamServer_IsRelayed(
        Dhcpv6MessageTypeEnum innerType
    )
    {
        await AssertRelayedAsync(
            innerType,
            IPAddress.IPv6Loopback,
            linkAddress: IPAddress.Parse("2001:db8:1::1"),
            expectRelayed: true
        );
    }

    [Fact]
    public void BuildClientDestination_ForALinkLocalPeer_ScopesItWithTheInterfaceIndex()
    {
        IPEndPoint destination = Dhcpv6RelayAgent.BuildClientDestination(
            IPAddress.Parse("fe80::1"),
            7,
            546
        );

        Assert.Equal(IPAddress.Parse("fe80::1%7"), destination.Address);
        Assert.Equal(7, destination.Address.ScopeId);
        Assert.Equal(546, destination.Port);
    }

    [Fact]
    public void BuildClientDestination_ForAGlobalPeer_IsUnchanged()
    {
        IPEndPoint destination = Dhcpv6RelayAgent.BuildClientDestination(
            IPAddress.Parse("2001:db8::1"),
            7,
            546
        );

        Assert.Equal(IPAddress.Parse("2001:db8::1"), destination.Address);
        Assert.Equal(0, destination.Address.ScopeId);
    }

    [Fact]
    public void BuildClientDestination_ForALinkLocalPeerWithNoKnownInterface_IsUnchanged()
    {
        IPEndPoint destination = Dhcpv6RelayAgent.BuildClientDestination(
            IPAddress.Parse("fe80::1"),
            null,
            546
        );

        Assert.Equal(IPAddress.Parse("fe80::1"), destination.Address);
        Assert.Equal(0, destination.Address.ScopeId);
    }

    [Fact]
    public void Constructor_WhenNoInterfaceOwnsTheLinkAddress_LogsOneWarning()
    {
        WarningCountingLogger<Dhcpv6RelayAgent> logger = new();

        using Dhcpv6RelayAgent agent = new(
            Options.Create(new Dhcpv6RelayOptions { Enable = true }),
            Options.Create(new Dhcpv6ProtocolOptions()),
            new FixedDhcpv6RelayAgentAddressProvider(IPAddress.Parse("2001:db8:1::1")),
            logger,
            interfaceIndexOf: _ => null
        );

        Assert.Single(logger.Warnings);
    }

    [Fact]
    public async Task HandleRelayMessageAsync_ForAScopelessLinkLocalPeer_ReachesTheClientOnItsLink()
    {
        // The test needs an interface that holds a bindable fe80:: address; without one it
        // returns early, so it reports as passed, not skipped.
        (IPAddress linkLocal, int interfaceIndex) = FindBindableLinkLocalAddress();

        if (linkLocal == null)
            return;

        using UdpClient clientSocket = new(new IPEndPoint(linkLocal, 0));
        int clientPort = ((IPEndPoint)clientSocket.Client.LocalEndPoint).Port;
        IPAddress linkAddress = IPAddress.Parse("2001:db8:1::1");

        using Dhcpv6RelayAgent agent = new(
            Options.Create(
                new Dhcpv6RelayOptions
                {
                    Enable = true,
                    UpstreamServerAddresses = ["::1"],
                    UpstreamServerPort = GetFreePort(),
                }
            ),
            Options.Create(new Dhcpv6ProtocolOptions { ClientPort = clientPort }),
            new FixedDhcpv6RelayAgentAddressProvider(linkAddress),
            NullLogger<Dhcpv6RelayAgent>.Instance,
            interfaceIndexOf: address =>
                ClientAddress.IsSameAddress(address, linkAddress) ? interfaceIndex : null
        );

        Dhcpv6Message inner = new() { MessageType = Dhcpv6MessageTypeEnum.Reply };
        inner.SetServerIdentifier([0x00, 0x02, 0x00, 0x00, 0xAB, 0xCD]);

        Dhcpv6RelayMessage relayReply = new()
        {
            MessageType = Dhcpv6MessageTypeEnum.RelayReply,
            LinkAddress = linkAddress,
            // The server copies the peer-address from the RELAY-FORW, which carries no scope.
            PeerAddress = new IPAddress(linkLocal.GetAddressBytes()),
        };
        relayReply.SetRelayMessagePayload(Dhcpv6MessageWriter.Write(inner));

        await agent.HandleRelayMessageAsync(
            relayReply,
            new IPEndPoint(IPAddress.IPv6Loopback, 547),
            CancellationToken.None
        );

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        UdpReceiveResult received = await clientSocket.ReceiveAsync(timeout.Token);
        Assert.True(Dhcpv6MessageReader.TryRead(received.Buffer, out Dhcpv6Message relayed, out _));
        Assert.Equal(Dhcpv6MessageTypeEnum.Reply, relayed.MessageType);
    }

    /// <summary>
    ///     This method finds the last up interface that holds a link-local IPv6 address a UDP
    ///     socket can bind, so a test can use a real link-local client. The last one is taken
    ///     because the system tends to pick the first interface for an unscoped link-local
    ///     destination, and the test only proves the scope ID when the answer differs.
    /// </summary>
    /// <returns>The scoped address and its interface index, or a null address when there is none.</returns>
    private static (IPAddress Address, int InterfaceIndex) FindBindableLinkLocalAddress()
    {
        if (!LocalAddresses.CanBind(IPAddress.IPv6Loopback))
            return (null, 0);

        IPAddress found = null;

        foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up)
                continue;

            foreach (
                UnicastIPAddressInformation unicast in networkInterface
                    .GetIPProperties()
                    .UnicastAddresses
            )
            {
                if (!unicast.Address.IsIPv6LinkLocal)
                    continue;

                try
                {
                    using UdpClient probe = new(new IPEndPoint(unicast.Address, 0));
                    found = unicast.Address;
                }
                catch (SocketException)
                {
                    // Not bindable (for example an address still in duplicate detection).
                }
            }
        }

        return found == null ? (null, 0) : (found, (int)found.ScopeId);
    }

    /// <summary>
    ///     This method hands the relay a RELAY-REPL addressed to a client socket on the loopback
    ///     address and says whether the inner message reached that socket.
    /// </summary>
    private static async Task AssertRelayedAsync(
        Dhcpv6MessageTypeEnum innerType,
        IPAddress source,
        IPAddress linkAddress,
        bool expectRelayed
    )
    {
        if (!LocalAddresses.CanBind(IPAddress.IPv6Loopback))
            return;

        using UdpClient clientSocket = new(new IPEndPoint(IPAddress.IPv6Loopback, 0));
        int clientPort = ((IPEndPoint)clientSocket.Client.LocalEndPoint).Port;

        using Dhcpv6RelayAgent agent = CreateAgent(
            upstreamServerAddresses: ["::1"],
            clientPort: clientPort
        );

        Dhcpv6Message inner = new() { MessageType = innerType };
        inner.SetServerIdentifier([0x00, 0x02, 0x00, 0x00, 0xAB, 0xCD]);

        Dhcpv6RelayMessage relayReply = new()
        {
            MessageType = Dhcpv6MessageTypeEnum.RelayReply,
            LinkAddress = linkAddress,
            PeerAddress = IPAddress.IPv6Loopback,
        };
        relayReply.SetRelayMessagePayload(Dhcpv6MessageWriter.Write(inner));

        await agent.HandleRelayMessageAsync(
            relayReply,
            new IPEndPoint(source, 547),
            CancellationToken.None
        );

        using CancellationTokenSource timeout = new(
            expectRelayed ? TimeSpan.FromSeconds(5) : TimeSpan.FromMilliseconds(300)
        );

        if (expectRelayed)
        {
            UdpReceiveResult received = await clientSocket.ReceiveAsync(timeout.Token);
            Assert.True(
                Dhcpv6MessageReader.TryRead(received.Buffer, out Dhcpv6Message relayed, out _)
            );
            Assert.Equal(innerType, relayed.MessageType);
        }
        else
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await clientSocket.ReceiveAsync(timeout.Token)
            );
        }
    }

    /// <summary>
    ///     This method builds a <see cref="Dhcpv6RelayAgent" /> by hand, without dependency
    ///     injection, with a fixed link address and the given upstream servers.
    /// </summary>
    private static Dhcpv6RelayAgent CreateAgent(
        IReadOnlyList<string> upstreamServerAddresses,
        int clientPort = 546
    ) =>
        new(
            Options.Create(
                new Dhcpv6RelayOptions
                {
                    Enable = true,
                    UpstreamServerAddresses = [.. upstreamServerAddresses],
                    // Never the real DHCPv6 server port: a DHCP server may be listening there.
                    UpstreamServerPort = GetFreePort(),
                }
            ),
            Options.Create(new Dhcpv6ProtocolOptions { ClientPort = clientPort }),
            new FixedDhcpv6RelayAgentAddressProvider(IPAddress.Parse("2001:db8:1::1")),
            NullLogger<Dhcpv6RelayAgent>.Instance
        );

    /// <summary>
    ///     This method asks the operating system for a currently unused port, by binding to port 0
    ///     on loopback and reading back what was assigned. Tests run on shared machines that host
    ///     DNS, DHCP and web services, and may run alongside another copy of this suite, so no
    ///     test uses a fixed or well-known port.
    /// </summary>
    /// <returns>A free TCP port number.</returns>
    private static int GetFreePort()
    {
        using TcpListener listener = new(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }
}
