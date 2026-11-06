using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Protocol.Dhcpv6;
using Ninlil.Dhcp.Server.Core.Tests.Fakes;
using Ninlil.Dns.Common.Enum;
// The alias is needed for the reason given in Dhcpv4UdpRoundTripTests.
using MicrosoftOptions = Microsoft.Extensions.Options.Options;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Tests;

/// <summary>
///     This test class checks the DHCPv6 engine (<see cref="Dhcpv6RequestHandler" />) together with
///     the UDP transport (<see cref="Dhcpv6UdpListener" />) over IPv6 loopback on high ports: a
///     SOLICIT-ADVERTISE-REQUEST-REPLY exchange followed by a RELEASE, and a server-address tag
///     rule option surviving the round trip. It uses a <see cref="Dhcpv6EngineTestHost" /> and is
///     the counterpart of <see cref="Dhcpv4UdpRoundTripTests" />.
/// </summary>
public sealed class Dhcpv6UdpRoundTripTests : IAsyncLifetime
{
    private Dhcpv6EngineTestHost _host;
    private Dhcpv6UdpListener _listener;
    private bool _ipv6LoopbackAvailable;
    private Dhcpv6ProtocolOptions _options;
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

        // Port 0 lets the operating system pick the server port and the listener report it, so no
        // other process can take a port number between a probe and the bind. The client port is
        // set in CreateClient, once the test's client socket has its own port.
        _options = new Dhcpv6ProtocolOptions { Enable = true, ServerPort = 0 };
        _listener = new Dhcpv6UdpListener(
            _host.Handler,
            MicrosoftOptions.Create(_options),
            NullLogger<Dhcpv6UdpListener>.Instance
        );

        await _listener.StartAsync(CancellationToken.None);
        _serverPort = _listener.BoundPort;

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
    public async Task SolicitThenRequest_ThenRelease_AllWorkOverARealSocket()
    {
        if (!_ipv6LoopbackAvailable)
            return;

        await _host.SeedAddressScopeAsync(
            "udp-round-trip-v6",
            "2001:db8:9999::10",
            "2001:db8:9999::11"
        );

        using UdpClient client = CreateClient();
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
        release.SetServerIdentifier(serverDuid);
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
        if (!_ipv6LoopbackAvailable)
            return;

        DhcpScopeEntity scope = await _host.SeedAddressScopeAsync(
            "udp-round-trip-v6-server-address",
            "2001:db8:9998::10",
            "2001:db8:9998::11"
        );

        // Code 23 (DNS Recursive Name Server) is server-address eligible. The scope has no
        // DnsServerAddresses, so option 23 in the reply can only come from the substitution.
        // This is the socket counterpart of
        // Dhcpv6OptionOverlayServiceTests.Solicit_WithServerAddressTagRuleOption_DeliversThisServersOwnAddress.
        await _host.SeedTagRuleAsync(
            "DNS via this server",
            new Dictionary<byte, string> { [23] = string.Empty },
            scopeId: scope.Id,
            serverAddressOptionTags: new HashSet<byte> { 23 }
        );

        using UdpClient client = CreateClient();
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
    ///     This constant is how long a test waits for a reply. A reply ends the wait at once, so
    ///     the length only matters when the machine is slow: the first request makes the engine
    ///     compile its database queries, which takes seconds when many test processes share a
    ///     host.
    /// </summary>
    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     This method opens a client socket on an OS-assigned IPv6 loopback port and tells the
    ///     listener to send replies there. The listener reads the client port from its options at
    ///     each send, so setting it after the listener started is enough.
    /// </summary>
    private UdpClient CreateClient()
    {
        UdpClient client = new(AddressFamily.InterNetworkV6);
        client.Client.Bind(new IPEndPoint(IPAddress.IPv6Loopback, 0));
        _options.ClientPort = ((IPEndPoint)client.Client.LocalEndPoint).Port;
        return client;
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

        using CancellationTokenSource receiveTimeout = new(ReceiveTimeout);
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
}
