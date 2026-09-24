using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Protocol.Dhcpv6;
// See Dhcpv4UdpRoundTripTests's own doc comment on this same alias for why it is needed.
using MicrosoftOptions = Microsoft.Extensions.Options.Options;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Tests;

/// <summary>
///     This test proves the real, receiving side of this project's own single-relay-hop DHCPv6
///     relay support end to end: a hand-built RELAY-FORW datagram, standing in for one a genuine
///     external relay agent would have sent, is sent to a real <see cref="Dhcpv6UdpListener" />
///     bound to the real <see cref="Dhcpv6RequestHandler" /> engine (against a real, SQLite-backed
///     <see cref="Dhcpv6EngineTestHost" />), and this test asserts the resulting RELAY-REPL is both
///     correctly scoped (the offered address comes from the one scope whose own subnet contains
///     the RELAY-FORW's own link-address -- <see cref="Dhcpv6ScopeResolverTests" /> already proves
///     that resolution step in isolation; this test proves it reached end to end through the real
///     socket/listener/engine path) and correctly wrapped (hop-count/link-address/peer-address
///     copied unchanged, per RFC 8415 Section 16). The genuinely different "forwarding side" --
///     this server itself acting as a relay agent -- is proven separately, in
///     <c>Ninlil.Dhcp.Server.Relay.Tests.Dhcpv6RelayRoundTripTests</c>.
/// </summary>
public sealed class Dhcpv6RelayReceivingRoundTripTests : IAsyncLifetime
{
    private static readonly IPAddress RelayLinkAddress = IPAddress.Parse("2001:db8:9999::1");
    private static readonly IPAddress ClientPeerAddress = IPAddress.Parse("fe80::4242");

    private Dhcpv6EngineTestHost _host;
    private Dhcpv6UdpListener _listener;
    private int _serverPort;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _host = new Dhcpv6EngineTestHost();
        await _host.InitializeAsync();

        // The one scope this relayed request should resolve to -- its own /64 contains
        // RelayLinkAddress, exactly the signal Dhcpv6ScopeResolver's new relay step reads.
        await _host.SeedAddressScopeAsync(
            "relay-receiving-scope",
            "2001:db8:9999::10",
            "2001:db8:9999::11"
        );

        _serverPort = GetFreeUdpPort();
        Dhcpv6ProtocolOptions options = new() { Enable = true, ServerPort = _serverPort };

        // The service provider is the real, full dependency injection container
        // Dhcpv6EngineTestHost already built -- IDhcpv6RelayMessageHandler resolves to the exact
        // same Dhcpv6RequestHandler singleton IDhcpv6RequestHandler already does (see
        // ServiceCollectionDhcpServerCoreExtension.AddNinlilDhcpv6ServerCore's own registration).
        _listener = new Dhcpv6UdpListener(
            _host.Handler,
            MicrosoftOptions.Create(options),
            NullLogger<Dhcpv6UdpListener>.Instance,
            _host.Host.Services
        );

        await _listener.StartAsync(CancellationToken.None);

        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!_listener.IsListening && DateTime.UtcNow < deadline)
            await Task.Delay(10);
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        await _listener.StopAsync(CancellationToken.None);
        await _host.DisposeAsync();
    }

    [Fact]
    public async Task RelayForwardCarryingASolicit_ProducesACorrectlyScopedAndWrappedRelayReply()
    {
        byte[] clientDuid = [0x00, 0x01, 0x00, 0x01, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF];

        Dhcpv6Message innerSolicit = new()
        {
            MessageType = Dhcpv6MessageTypeEnum.Solicit,
            TransactionId = 0x1234,
        };
        innerSolicit.SetClientIdentifier(clientDuid);
        innerSolicit.SetElapsedTime(TimeSpan.Zero);
        innerSolicit.AddIdentityAssociationForNonTemporaryAddresses(
            new Dhcpv6IdentityAssociationForNonTemporaryAddresses { IdentityAssociationId = 1 }
        );

        Dhcpv6RelayMessage relayForward = new()
        {
            MessageType = Dhcpv6MessageTypeEnum.RelayForward,
            HopCount = 0,
            LinkAddress = RelayLinkAddress,
            PeerAddress = ClientPeerAddress,
        };
        relayForward.SetRelayMessagePayload(Dhcpv6MessageWriter.Write(innerSolicit));
        relayForward.SetInterfaceId([0x65, 0x74, 0x68, 0x30]); // "eth0"

        byte[] relayForwardBytes = Dhcpv6RelayMessageWriter.Write(relayForward);

        using Socket sender = new(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
        sender.Bind(new IPEndPoint(IPAddress.IPv6Loopback, 0));

        await sender.SendToAsync(
            relayForwardBytes,
            new IPEndPoint(IPAddress.IPv6Loopback, _serverPort)
        );

        byte[] receiveBuffer = new byte[4096];
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        SocketReceiveFromResult received = await sender.ReceiveFromAsync(
            receiveBuffer,
            new IPEndPoint(IPAddress.IPv6Any, 0),
            timeout.Token
        );

        byte[] replyBytes = receiveBuffer[..received.ReceivedBytes];

        Assert.True(
            Dhcpv6RelayMessageReader.TryRead(
                replyBytes,
                out Dhcpv6RelayMessage relayReply,
                out string failureReason
            ),
            failureReason
        );
        Assert.Equal(Dhcpv6MessageTypeEnum.RelayReply, relayReply.MessageType);
        // RFC 8415 Section 16: hop-count/link-address/peer-address copied unchanged.
        Assert.Equal(relayForward.HopCount, relayReply.HopCount);
        Assert.Equal(RelayLinkAddress, relayReply.LinkAddress);
        Assert.Equal(ClientPeerAddress, relayReply.PeerAddress);
        // The Interface-ID the (simulated) relay agent sent must be echoed back unchanged.
        Assert.True(relayReply.TryGetInterfaceId(out byte[] echoedInterfaceId));
        Assert.Equal([0x65, 0x74, 0x68, 0x30], echoedInterfaceId);

        Assert.True(relayReply.TryGetRelayMessagePayload(out byte[] innerReplyPayload));
        Assert.True(
            Dhcpv6MessageReader.TryRead(
                innerReplyPayload,
                out Dhcpv6Message innerReply,
                out string innerFailureReason
            ),
            innerFailureReason
        );
        Assert.Equal(Dhcpv6MessageTypeEnum.Advertise, innerReply.MessageType);
        Assert.Equal(innerSolicit.TransactionId, innerReply.TransactionId);

        Assert.True(
            innerReply.TryGetIdentityAssociationsForNonTemporaryAddresses(
                out IReadOnlyList<Dhcpv6IdentityAssociationForNonTemporaryAddresses> offered
            )
        );
        Assert.Single(offered);
        Assert.Single(offered[0].Addresses);
        // The offered address must come from relay-receiving-scope's own pool -- proving the
        // request really was resolved by RelayLinkAddress's own subnet containment, not some other
        // fallback.
        Assert.Equal(IPAddress.Parse("2001:db8:9999::10"), offered[0].Addresses[0].Address);
    }

    /// <summary>
    ///     This method obtains a currently-unused, unprivileged IPv6 UDP port, the same convention
    ///     <c>Dhcpv6UdpRoundTripTests.GetFreeUdpPort</c> already establishes.
    /// </summary>
    private static int GetFreeUdpPort()
    {
        using Socket probe = new(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
        probe.Bind(new IPEndPoint(IPAddress.IPv6Loopback, 0));
        return ((IPEndPoint)probe.LocalEndPoint).Port;
    }
}
