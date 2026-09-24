using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Protocol.Dhcpv6;
using Ninlil.Dns.Common.Enum;
// See Dhcpv4UdpRoundTripTests's own doc comment on this same alias for why it is needed.
using MicrosoftOptions = Microsoft.Extensions.Options.Options;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Tests;

/// <summary>
///     This test proves the real DHCPv6 engine (<see cref="Dhcpv6RequestHandler" />) and the real
///     UDP transport (<see cref="Dhcpv6UdpListener" />) genuinely work together over a real
///     socket: a full SOLICIT-Advertise-Request-Reply exchange followed by a RELEASE, against a
///     real, SQLite-backed <see cref="Dhcpv6EngineTestHost" /> -- the DHCPv6 analogue of
///     <see cref="Dhcpv4UdpRoundTripTests" />.
/// </summary>
public sealed class Dhcpv6UdpRoundTripTests : IAsyncLifetime
{
    private Dhcpv6EngineTestHost _host;
    private Dhcpv6UdpListener _listener;
    private int _serverPort;
    private int _clientPort;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _host = new Dhcpv6EngineTestHost();
        await _host.InitializeAsync();

        _serverPort = GetFreeUdpPort();
        _clientPort = GetFreeUdpPort();

        Dhcpv6ProtocolOptions options = new()
        {
            Enable = true,
            ServerPort = _serverPort,
            ClientPort = _clientPort,
        };
        _listener = new Dhcpv6UdpListener(
            _host.Handler,
            MicrosoftOptions.Create(options),
            NullLogger<Dhcpv6UdpListener>.Instance
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
    public async Task SolicitThenRequest_ThenRelease_AllWorkOverARealSocket()
    {
        await _host.SeedAddressScopeAsync(
            "udp-round-trip-v6",
            "2001:db8:9999::10",
            "2001:db8:9999::11"
        );

        using UdpClient client = new(AddressFamily.InterNetworkV6);
        client.Client.Bind(new IPEndPoint(IPAddress.IPv6Loopback, _clientPort));
        byte[] clientDuid = [0x00, 0x01, 0x00, 0x01, 0xde, 0xad, 0xbe, 0xef, 0x00, 0x01];

        // 1. SOLICIT -> ADVERTISE.
        Dhcpv6Message solicit = new()
        {
            MessageType = Dhcpv6MessageTypeEnum.Solicit,
            TransactionId = 0x1111,
        };
        solicit.SetClientIdentifier(clientDuid);
        solicit.SetElapsedTime(TimeSpan.Zero);
        solicit.AddIdentityAssociationForNonTemporaryAddresses(
            new Dhcpv6IdentityAssociationForNonTemporaryAddresses { IdentityAssociationId = 1 }
        );

        Dhcpv6Message advertise = await SendAndReceiveAsync(client, solicit);

        Assert.Equal(Dhcpv6MessageTypeEnum.Advertise, advertise.MessageType);
        advertise.TryGetServerIdentifier(out byte[] serverDuid);
        advertise.TryGetIdentityAssociationsForNonTemporaryAddresses(
            out IReadOnlyList<Dhcpv6IdentityAssociationForNonTemporaryAddresses> offered
        );
        IPAddress offeredAddress = offered[0].Addresses[0].Address;
        Assert.Equal(IPAddress.Parse("2001:db8:9999::10"), offeredAddress);

        // 2. REQUEST -> REPLY.
        Dhcpv6Message request = new()
        {
            MessageType = Dhcpv6MessageTypeEnum.Request,
            TransactionId = 0x1111,
        };
        request.SetClientIdentifier(clientDuid);
        request.SetServerIdentifier(serverDuid);
        Dhcpv6IdentityAssociationForNonTemporaryAddresses requestNa = new()
        {
            IdentityAssociationId = 1,
        };
        requestNa.Addresses.Add(new Dhcpv6IdentityAssociationAddress { Address = offeredAddress });
        request.AddIdentityAssociationForNonTemporaryAddresses(requestNa);

        Dhcpv6Message reply = await SendAndReceiveAsync(client, request);

        Assert.Equal(Dhcpv6MessageTypeEnum.Reply, reply.MessageType);
        reply.TryGetIdentityAssociationsForNonTemporaryAddresses(
            out IReadOnlyList<Dhcpv6IdentityAssociationForNonTemporaryAddresses> committed
        );
        Assert.Equal(offeredAddress, committed[0].Addresses[0].Address);

        LeaseEntity activeLease = await _host.FindLeaseAsync(
            "udp-round-trip-v6",
            string.Join(':', clientDuid.Select(b => b.ToString("x2")))
        );
        Assert.NotNull(activeLease);
        Assert.Equal(LeaseStateEnum.Active, activeLease.State);

        // 3. RELEASE -> REPLY with a Success status.
        Dhcpv6Message release = new()
        {
            MessageType = Dhcpv6MessageTypeEnum.Release,
            TransactionId = 0x2222,
        };
        release.SetClientIdentifier(clientDuid);
        Dhcpv6IdentityAssociationForNonTemporaryAddresses releaseNa = new()
        {
            IdentityAssociationId = 1,
        };
        releaseNa.Addresses.Add(new Dhcpv6IdentityAssociationAddress { Address = offeredAddress });
        release.AddIdentityAssociationForNonTemporaryAddresses(releaseNa);

        Dhcpv6Message releaseReply = await SendAndReceiveAsync(client, release);
        Assert.Equal(Dhcpv6MessageTypeEnum.Reply, releaseReply.MessageType);
    }

    [Fact]
    public async Task Solicit_WithServerAddressTagRuleOption_DeliversThisServersOwnAddressOverARealSocket()
    {
        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "udp-round-trip-v6-server-address",
            "2001:db8:9998::10",
            "2001:db8:9998::11"
        );

        // Code 23 (DNS Recursive Name Server) is a real, server-address-eligible catalog option --
        // deliberately not configured on the scope's own DnsServerAddresses at all, so this reply's
        // own option 23 can only have come from the ServerAddress substitution. This is the
        // genuine, real-socket counterpart to
        // Dhcpv6OptionOverlayServiceTests.Solicit_WithServerAddressTagRuleOption_DeliversThisServersOwnAddress,
        // proving the same substitution survives a real UDP encode/decode round trip.
        await _host.SeedTagRuleAsync(
            "DNS via this server",
            new Dictionary<byte, string> { [23] = string.Empty },
            scopeId: scope.Id,
            serverAddressOptionTags: new HashSet<byte> { 23 }
        );

        using UdpClient client = new(AddressFamily.InterNetworkV6);
        client.Client.Bind(new IPEndPoint(IPAddress.IPv6Loopback, _clientPort));
        byte[] clientDuid = [0x00, 0x01, 0x00, 0x01, 0xde, 0xad, 0xbe, 0xef, 0x00, 0x02];

        Dhcpv6Message solicit = new()
        {
            MessageType = Dhcpv6MessageTypeEnum.Solicit,
            TransactionId = 0x5555,
        };
        solicit.SetClientIdentifier(clientDuid);
        solicit.SetElapsedTime(TimeSpan.Zero);
        solicit.AddIdentityAssociationForNonTemporaryAddresses(
            new Dhcpv6IdentityAssociationForNonTemporaryAddresses { IdentityAssociationId = 1 }
        );

        Dhcpv6Message advertise = await SendAndReceiveAsync(client, solicit);

        Assert.Equal(Dhcpv6MessageTypeEnum.Advertise, advertise.MessageType);
        Assert.True(advertise.TryGetRawOption(23, out byte[] rawDnsServerOption));
        Assert.Equal(
            IPAddress.Parse(Dhcpv6EngineTestHost.ServerAddress).GetAddressBytes(),
            rawDnsServerOption
        );
    }

    /// <summary>
    ///     This method sends a request datagram and waits for the corresponding reply, decoding it
    ///     back into a <see cref="Dhcpv6Message" />.
    /// </summary>
    private async Task<Dhcpv6Message> SendAndReceiveAsync(UdpClient client, Dhcpv6Message request)
    {
        byte[] bytes = Dhcpv6MessageWriter.Write(request);
        await client.SendAsync(
            bytes,
            bytes.Length,
            new IPEndPoint(IPAddress.IPv6Loopback, _serverPort)
        );

        using CancellationTokenSource receiveTimeout = new(TimeSpan.FromSeconds(5));
        UdpReceiveResult received = await client.ReceiveAsync(receiveTimeout.Token);

        Assert.True(
            Dhcpv6MessageReader.TryRead(
                received.Buffer,
                out Dhcpv6Message reply,
                out string reason
            ),
            reason
        );
        return reply;
    }

    /// <summary>
    ///     This method obtains a currently-unused, unprivileged IPv6 UDP port, the same way
    ///     <c>Ninlil.Dhcp.Protocol.Dhcpv6.Tests.Dhcpv6UdpRoundTripTests.GetFreeUdpPort</c> already
    ///     does.
    /// </summary>
    private static int GetFreeUdpPort()
    {
        using Socket probe = new(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
        probe.Bind(new IPEndPoint(IPAddress.IPv6Loopback, 0));
        return ((IPEndPoint)probe.LocalEndPoint).Port;
    }
}
