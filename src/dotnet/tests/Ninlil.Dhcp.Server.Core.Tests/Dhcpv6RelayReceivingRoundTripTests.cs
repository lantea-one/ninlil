using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Protocol.Dhcpv6;
using Ninlil.Dhcp.Server.Core.Tests.Fakes;
// The alias is needed for the reason given in Dhcpv4UdpRoundTripTests.
using MicrosoftOptions = Microsoft.Extensions.Options.Options;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Tests;

/// <summary>
///     This test class covers the receiving side of DHCPv6 relay support over IPv6 loopback: a
///     hand-built RELAY-FORW, as an external relay agent would send, goes to a
///     <see cref="Dhcpv6UdpListener" /> with the <see cref="Dhcpv6RequestHandler" /> engine on a
///     <see cref="Dhcpv6EngineTestHost" />, and the test checks the RELAY-REPL: hop count, link
///     address and peer address copied, Interface-ID echoed, and an address offered from the
///     scope. Only one scope exists, so this does not tell link-address resolution apart from the
///     single-scope fallback; <see cref="Dhcpv6ScopeResolverTests" /> covers that step. The
///     forwarding side (Ninlil as relay agent) is tested in
///     <c>Ninlil.Dhcp.Server.Relay.Tests.Dhcpv6RelayRoundTripTests</c>.
/// </summary>
public sealed class Dhcpv6RelayReceivingRoundTripTests : IAsyncLifetime
{
    private static readonly IPAddress RelayLinkAddress = IPAddress.Parse("2001:db8:9999::1");
    private static readonly IPAddress ClientPeerAddress = IPAddress.Parse("fe80::4242");

    private Dhcpv6EngineTestHost _host;
    private Dhcpv6UdpListener _listener;
    private bool _ipv6LoopbackAvailable;
    private int _serverPort;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _host = new Dhcpv6EngineTestHost();
        await _host.InitializeAsync();

        // Without an IPv6 loopback there is no socket to open; the tests return early and
        // DisposeAsync skips the listener.
        _ipv6LoopbackAvailable = LocalAddresses.CanBind(IPAddress.IPv6Loopback);
        if (!_ipv6LoopbackAvailable)
            return;

        // The scope the relayed request should resolve to; its /64 contains RelayLinkAddress.
        await _host.SeedAddressScopeAsync(
            "relay-receiving-scope",
            "2001:db8:9999::10",
            "2001:db8:9999::11"
        );

        _serverPort = GetFreeUdpPort();
        Dhcpv6ProtocolOptions options = new() { Enable = true, ServerPort = _serverPort };

        // Pass the host's service provider so the listener finds IDhcpv6RelayMessageHandler,
        // which is the same Dhcpv6RequestHandler singleton (see AddNinlilDhcpv6ServerCore).
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
        if (_listener is not null)
            await _listener.StopAsync(CancellationToken.None);

        await _host.DisposeAsync();
    }

    [Fact]
    public async Task RelayForwardCarryingASolicit_ProducesACorrectlyScopedAndWrappedRelayReply()
    {
        if (!_ipv6LoopbackAvailable)
            return;

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
        // Hop count, link address and peer address are copied unchanged (RFC 8415).
        Assert.Equal(relayForward.HopCount, relayReply.HopCount);
        Assert.Equal(RelayLinkAddress, relayReply.LinkAddress);
        Assert.Equal(ClientPeerAddress, relayReply.PeerAddress);
        // The Interface-ID the relay agent sent must be echoed unchanged.
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
        // The offered address comes from relay-receiving-scope's pool (with one scope, the
        // single-scope fallback would also choose it).
        Assert.Equal(IPAddress.Parse("2001:db8:9999::10"), offered[0].Addresses[0].Address);
    }

    /// <summary>
    ///     This method gets an unused high IPv6 UDP port, as
    ///     <c>Dhcpv6UdpRoundTripTests.GetFreeUdpPort</c> does.
    /// </summary>
    private static int GetFreeUdpPort()
    {
        using Socket probe = new(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
        probe.Bind(new IPEndPoint(IPAddress.IPv6Loopback, 0));
        return ((IPEndPoint)probe.LocalEndPoint).Port;
    }
}
