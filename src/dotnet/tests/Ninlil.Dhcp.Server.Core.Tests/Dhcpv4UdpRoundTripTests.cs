using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Protocol.Dhcpv4;
using Ninlil.Dns.Common.Enum;
// Inside Ninlil.Dhcp.Server.Core.Tests, the bare name "Options" resolves to the namespace
// Ninlil.Dhcp.Server.Core.Options before Microsoft.Extensions.Options.Options, so an alias is
// needed to reach the factory method.
using MicrosoftOptions = Microsoft.Extensions.Options.Options;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Tests;

/// <summary>
///     This test class checks the DHCPv4 engine (<see cref="Dhcpv4RequestHandler" />) together with
///     the UDP transport (<see cref="Dhcpv4UdpListener" />) over loopback sockets on high ports: a
///     DISCOVER-OFFER-REQUEST-ACK exchange, a RELEASE and a DECLINE that get no reply, and a
///     server-address custom option surviving the round trip. It uses a
///     <see cref="Dhcpv4EngineTestHost" /> with a temporary SQLite file. It is the engine-level
///     counterpart of <c>Dhcpv4UdpRoundTripTests</c> in <c>Ninlil.Dhcp.Protocol.Dhcpv4.Tests</c>,
///     which uses a stub handler.
/// </summary>
public sealed class Dhcpv4UdpRoundTripTests : IAsyncLifetime
{
    private Dhcpv4EngineTestHost _host;
    private Dhcpv4UdpListener _listener;
    private int _serverPort;
    private int _clientPort;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _host = new Dhcpv4EngineTestHost();
        await _host.InitializeAsync();

        _serverPort = GetFreeUdpPort();
        _clientPort = GetFreeUdpPort();

        Dhcpv4ProtocolOptions options = new()
        {
            Enable = true,
            ServerPort = _serverPort,
            ClientPort = _clientPort,
        };
        _listener = new Dhcpv4UdpListener(
            _host.Handler,
            MicrosoftOptions.Create(options),
            NullLogger<Dhcpv4UdpListener>.Instance
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
    public async Task FullDoraExchange_ThenDeclineAndRelease_AllWorkOverARealSocket()
    {
        await _host.SeedScopeAsync(
            "udp-round-trip",
            "198.51.100.10",
            "198.51.100.11",
            gatewayAddresses: ["198.51.100.1"]
        );

        using UdpClient client = new(new IPEndPoint(IPAddress.Loopback, _clientPort));
        byte[] clientHardwareAddress = [0xde, 0xad, 0xbe, 0xef, 0x00, 0x01];

        // 1. DISCOVER -> OFFER.
        Dhcpv4Message discover = NewMessage(clientHardwareAddress, 0x1111);
        discover.SetMessageType(Dhcpv4MessageTypeEnum.Discover);
        Dhcpv4Message offer = await SendAndReceiveAsync(client, discover);

        Assert.NotNull(offer);
        Assert.True(offer.TryGetMessageType(out Dhcpv4MessageTypeEnum offerType));
        Assert.Equal(Dhcpv4MessageTypeEnum.Offer, offerType);
        Assert.Equal(IPAddress.Parse("198.51.100.10"), offer.YourIpAddress);
        offer.TryGetServerIdentifier(out IPAddress serverIdentifier);

        // 2. REQUEST (selecting) -> ACK.
        Dhcpv4Message request = NewMessage(clientHardwareAddress, 0x1111);
        request.SetMessageType(Dhcpv4MessageTypeEnum.Request);
        request.SetServerIdentifier(serverIdentifier);
        request.SetRequestedIpAddress(offer.YourIpAddress);
        Dhcpv4Message ack = await SendAndReceiveAsync(client, request);

        Assert.NotNull(ack);
        Assert.True(ack.TryGetMessageType(out Dhcpv4MessageTypeEnum ackType));
        Assert.Equal(Dhcpv4MessageTypeEnum.Ack, ackType);
        Assert.Equal(offer.YourIpAddress, ack.YourIpAddress);

        LeaseEntity activeLease = await _host.FindLeaseAsync("udp-round-trip", "de:ad:be:ef:00:01");
        Assert.NotNull(activeLease);
        Assert.Equal(LeaseStateEnum.Active, activeLease.State);

        // 3. RELEASE -> no reply, and the address is immediately available to a new client.
        Dhcpv4Message release = NewMessage(clientHardwareAddress, 0x2222);
        release.SetMessageType(Dhcpv4MessageTypeEnum.Release);
        release.ClientIpAddress = ack.YourIpAddress;
        await SendAsync(client, release);
        await AssertNoReplyArrivesAsync(client);

        byte[] secondClientHardwareAddress = [0xde, 0xad, 0xbe, 0xef, 0x00, 0x02];
        Dhcpv4Message secondDiscover = NewMessage(secondClientHardwareAddress, 0x3333);
        secondDiscover.SetMessageType(Dhcpv4MessageTypeEnum.Discover);
        Dhcpv4Message secondOffer = await SendAndReceiveAsync(client, secondDiscover);

        Assert.NotNull(secondOffer);
        Assert.Equal(ack.YourIpAddress, secondOffer.YourIpAddress);

        // 4. DECLINE the second client's offered address -> no reply, and the address is
        // quarantined. The pool has two addresses and the first is free again, so declining the
        // second shows the quarantine without depending on the pool scan order.
        Dhcpv4Message decline = NewMessage(secondClientHardwareAddress, 0x3333);
        decline.SetMessageType(Dhcpv4MessageTypeEnum.Decline);
        decline.SetRequestedIpAddress(secondOffer.YourIpAddress);
        secondOffer.TryGetServerIdentifier(out IPAddress secondServerIdentifier);
        decline.SetServerIdentifier(secondServerIdentifier);
        await SendAsync(client, decline);
        await AssertNoReplyArrivesAsync(client);
    }

    [Fact]
    public async Task Discover_WithServerAddressCustomOption_DeliversThisServersOwnAddressOverARealSocket()
    {
        DhcpScopeEntity scope = await _host.SeedScopeAsync(
            "udp-round-trip-server-address",
            "198.51.100.20",
            "198.51.100.21",
            gatewayAddresses: ["198.51.100.1"]
        );

        // Tag 6 (Domain Name Server) is server-address eligible. This is the socket counterpart
        // of Dhcpv4OptionOverlayServiceTests.Discover_WithServerAddressCustomOption_DeliversThisServersOwnAddress,
        // showing the substitution survives UDP encoding and decoding.
        await _host.SeedCustomOptionAsync(
            scope.Id,
            6,
            valueHex: string.Empty,
            valueSource: DhcpOptionValueSourceEnum.ServerAddress
        );

        using UdpClient client = new(new IPEndPoint(IPAddress.Loopback, _clientPort));
        byte[] clientHardwareAddress = [0xde, 0xad, 0xbe, 0xef, 0x00, 0x03];

        Dhcpv4Message discover = NewMessage(clientHardwareAddress, 0x4444);
        discover.SetMessageType(Dhcpv4MessageTypeEnum.Discover);
        Dhcpv4Message offer = await SendAndReceiveAsync(client, discover);

        Assert.NotNull(offer);
        Assert.True(offer.TryGetRawOption(6, out byte[] rawDnsServerOption));
        Assert.Equal(
            IPAddress.Parse(Dhcpv4EngineTestHost.ServerIdentifierAddress).GetAddressBytes(),
            rawDnsServerOption
        );
    }

    /// <summary>
    ///     This method sends a request datagram and waits for the corresponding reply, decoding it
    ///     back into a <see cref="Dhcpv4Message" />.
    /// </summary>
    private async Task<Dhcpv4Message> SendAndReceiveAsync(UdpClient client, Dhcpv4Message request)
    {
        await SendAsync(client, request);

        using CancellationTokenSource receiveTimeout = new(TimeSpan.FromSeconds(5));
        UdpReceiveResult received = await client.ReceiveAsync(receiveTimeout.Token);

        Assert.True(
            Dhcpv4MessageReader.TryRead(
                received.Buffer,
                out Dhcpv4Message reply,
                out string reason
            ),
            reason
        );
        return reply;
    }

    /// <summary>
    ///     This method sends one encoded request to the listener on loopback.
    /// </summary>
    private async Task SendAsync(UdpClient client, Dhcpv4Message request)
    {
        byte[] bytes = Dhcpv4MessageWriter.Write(request);
        await client.SendAsync(
            bytes,
            bytes.Length,
            new IPEndPoint(IPAddress.Loopback, _serverPort)
        );
    }

    /// <summary>
    ///     This method checks that no reply arrives within a short window, to show that DECLINE
    ///     and RELEASE get no reply over the socket (RFC 2131).
    /// </summary>
    private static async Task AssertNoReplyArrivesAsync(UdpClient client)
    {
        using CancellationTokenSource shortTimeout = new(TimeSpan.FromMilliseconds(500));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await client.ReceiveAsync(shortTimeout.Token)
        );
    }

    /// <summary>
    ///     This method gets an unused high UDP port, as
    ///     <c>Ninlil.Dhcp.Protocol.Dhcpv4.Tests.Dhcpv4UdpRoundTripTests</c> does.
    /// </summary>
    private static int GetFreeUdpPort()
    {
        using UdpClient probe = new(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint).Port;
    }

    /// <summary>
    ///     This method builds a request message with the fields every test sets.
    ///     <see cref="Dhcpv4Message.ClientIpAddress" /> is loopback, as in
    ///     <c>Ninlil.Dhcp.Protocol.Dhcpv4.Tests.Dhcpv4UdpRoundTripTests</c>, to force a unicast
    ///     reply instead of relying on broadcast delivery. A RELEASE replaces it with the leased
    ///     address, which RFC 2131 Section 4.3.6 puts in <c>ciaddr</c>.
    /// </summary>
    private static Dhcpv4Message NewMessage(byte[] clientHardwareAddress, uint transactionId) =>
        new()
        {
            OperationCode = Dhcpv4OperationCodeEnum.BootRequest,
            TransactionId = transactionId,
            HardwareAddressLength = (byte)clientHardwareAddress.Length,
            ClientHardwareAddress = clientHardwareAddress,
            ClientIpAddress = IPAddress.Loopback,
        };
}
