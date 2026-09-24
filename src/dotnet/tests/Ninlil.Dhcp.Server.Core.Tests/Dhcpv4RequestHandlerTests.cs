using System.Net;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Tests;

/// <summary>
///     This test proves the real DHCPv4 server engine's own message-type state machine end to
///     end, driving <see cref="Dhcpv4RequestHandler.HandleRequestAsync" /> directly (bypassing the
///     real UDP socket layer, which <c>Ninlil.Dhcp.Protocol.Dhcpv4.Tests</c> and this project's own
///     <see cref="Dhcpv4UdpRoundTripTests" /> already prove separately) against a real,
///     SQLite-backed <see cref="Dhcpv4EngineTestHost" /> -- real repositories, real
///     <c>SaveChangesAsync</c> commits, real reads back from storage, not mocked collaborators.
/// </summary>
public sealed class Dhcpv4RequestHandlerTests : IAsyncLifetime
{
    private static readonly IPEndPoint RemoteEndPoint = new(IPAddress.Loopback, 68);

    private Dhcpv4EngineTestHost _host;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _host = new Dhcpv4EngineTestHost();
        await _host.InitializeAsync();
    }

    /// <inheritdoc />
    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Discover_WithFreePool_OffersFirstAvailableAddressAndStandardOptions()
    {
        await _host.SeedScopeAsync(
            "test",
            "192.0.2.10",
            "192.0.2.12",
            gatewayAddresses: ["192.0.2.1"],
            dnsServerAddresses: ["192.0.2.1", "192.0.2.2"]
        );

        byte[] clientHardwareAddress = [0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0x01];
        Dhcpv4Message discover = NewMessage(clientHardwareAddress, 0x1000);
        discover.SetMessageType(Dhcpv4MessageTypeEnum.Discover);
        discover.SetParameterRequestList([1, 3, 6]);

        Dhcpv4Message offer = await _host.Handler.HandleRequestAsync(
            discover,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(offer);
        Assert.True(offer.TryGetMessageType(out Dhcpv4MessageTypeEnum messageType));
        Assert.Equal(Dhcpv4MessageTypeEnum.Offer, messageType);
        Assert.Equal(discover.TransactionId, offer.TransactionId);
        Assert.Equal(IPAddress.Parse("192.0.2.10"), offer.YourIpAddress);
        Assert.True(offer.TryGetServerIdentifier(out IPAddress serverIdentifier));
        Assert.Equal(
            IPAddress.Parse(Dhcpv4EngineTestHost.ServerIdentifierAddress),
            serverIdentifier
        );
        Assert.True(offer.TryGetSubnetMask(out IPAddress subnetMask));
        Assert.Equal(IPAddress.Parse("255.255.255.0"), subnetMask);
        Assert.True(offer.TryGetRouters(out IReadOnlyList<IPAddress> routers));
        Assert.Equal(IPAddress.Parse("192.0.2.1"), Assert.Single(routers));
        Assert.True(offer.TryGetDomainNameServers(out IReadOnlyList<IPAddress> dnsServers));
        Assert.Equal(2, dnsServers.Count);
        Assert.True(offer.TryGetIpAddressLeaseTime(out TimeSpan leaseTime));
        Assert.Equal(TimeSpan.FromHours(1), leaseTime);

        LeaseEntity lease = await _host.FindLeaseAsync("test", "aa:bb:cc:dd:ee:01");
        Assert.NotNull(lease);
        Assert.Equal(LeaseStateEnum.Offered, lease.State);
        Assert.Equal("192.0.2.10", lease.IpAddress);
    }

    [Fact]
    public async Task Discover_WhenPoolExhausted_ReturnsNoOffer()
    {
        DhcpScopeEntity scope = await _host.SeedScopeAsync("full", "192.0.2.20", "192.0.2.20");

        // Fill the pool's only address with a different, already-active client first.
        await ConfirmAddressForNewClientAsync(scope, [0x01, 0x02, 0x03, 0x04, 0x05, 0x06]);

        Dhcpv4Message discover = NewMessage([0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f], 0x2000);
        discover.SetMessageType(Dhcpv4MessageTypeEnum.Discover);

        Dhcpv4Message offer = await _host.Handler.HandleRequestAsync(
            discover,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.Null(offer);
    }

    [Fact]
    public async Task Discover_ForClientWithReservation_AlwaysOffersReservedAddress()
    {
        DhcpScopeEntity scope = await _host.SeedScopeAsync(
            "reserved-scope",
            "192.0.2.30",
            "192.0.2.32"
        );
        byte[] clientHardwareAddress = [0x11, 0x22, 0x33, 0x44, 0x55, 0x66];
        await _host.SeedReservationAsync(scope.Id, "11:22:33:44:55:66", "192.0.2.99");

        Dhcpv4Message discover = NewMessage(clientHardwareAddress, 0x3000);
        discover.SetMessageType(Dhcpv4MessageTypeEnum.Discover);

        Dhcpv4Message offer = await _host.Handler.HandleRequestAsync(
            discover,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(offer);
        Assert.Equal(IPAddress.Parse("192.0.2.99"), offer.YourIpAddress);
    }

    [Fact]
    public async Task Discover_ThenRequestSelecting_ConfirmsActiveLeaseWithNegotiatedLeaseTime()
    {
        await _host.SeedScopeAsync(
            "selecting-scope",
            "192.0.2.40",
            "192.0.2.42",
            defaultLeaseDuration: TimeSpan.FromHours(1),
            maxLeaseDuration: TimeSpan.FromHours(2)
        );

        byte[] clientHardwareAddress = [0x22, 0x22, 0x22, 0x22, 0x22, 0x22];
        Dhcpv4Message discover = NewMessage(clientHardwareAddress, 0x4000);
        discover.SetMessageType(Dhcpv4MessageTypeEnum.Discover);

        Dhcpv4Message offer = await _host.Handler.HandleRequestAsync(
            discover,
            RemoteEndPoint,
            CancellationToken.None
        );
        Assert.NotNull(offer);
        Assert.True(offer.TryGetServerIdentifier(out IPAddress serverIdentifier));

        Dhcpv4Message request = NewMessage(clientHardwareAddress, 0x4000);
        request.SetMessageType(Dhcpv4MessageTypeEnum.Request);
        request.SetServerIdentifier(serverIdentifier);
        request.SetRequestedIpAddress(offer.YourIpAddress);

        // The client asks for a shorter lease than the scope's own default -- RFC 2131 Section
        // 4.3.1 says the server must not extend it beyond what was requested.
        request.SetIpAddressLeaseTime(TimeSpan.FromMinutes(30));

        Dhcpv4Message ack = await _host.Handler.HandleRequestAsync(
            request,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(ack);
        Assert.True(ack.TryGetMessageType(out Dhcpv4MessageTypeEnum messageType));
        Assert.Equal(Dhcpv4MessageTypeEnum.Ack, messageType);
        Assert.Equal(offer.YourIpAddress, ack.YourIpAddress);
        Assert.True(ack.TryGetIpAddressLeaseTime(out TimeSpan leaseTime));
        Assert.Equal(TimeSpan.FromMinutes(30), leaseTime);

        LeaseEntity lease = await _host.FindLeaseAsync("selecting-scope", "22:22:22:22:22:22");
        Assert.NotNull(lease);
        Assert.Equal(LeaseStateEnum.Active, lease.State);
        Assert.NotNull(lease.LeaseObtainedAtUtc);
        Assert.True(lease.LeaseExpiresAtUtc > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Request_ClampsLeaseTimeRequestedBeyondScopeMaximum()
    {
        await _host.SeedScopeAsync(
            "clamp-scope",
            "192.0.2.50",
            "192.0.2.52",
            defaultLeaseDuration: TimeSpan.FromHours(1),
            maxLeaseDuration: TimeSpan.FromHours(2)
        );

        byte[] clientHardwareAddress = [0x33, 0x33, 0x33, 0x33, 0x33, 0x33];
        Dhcpv4Message discover = NewMessage(clientHardwareAddress, 0x5000);
        discover.SetMessageType(Dhcpv4MessageTypeEnum.Discover);
        Dhcpv4Message offer = await _host.Handler.HandleRequestAsync(
            discover,
            RemoteEndPoint,
            CancellationToken.None
        );
        offer.TryGetServerIdentifier(out IPAddress serverIdentifier);

        Dhcpv4Message request = NewMessage(clientHardwareAddress, 0x5000);
        request.SetMessageType(Dhcpv4MessageTypeEnum.Request);
        request.SetServerIdentifier(serverIdentifier);
        request.SetRequestedIpAddress(offer.YourIpAddress);
        request.SetIpAddressLeaseTime(TimeSpan.FromHours(10));

        Dhcpv4Message ack = await _host.Handler.HandleRequestAsync(
            request,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(ack);
        Assert.True(ack.TryGetIpAddressLeaseTime(out TimeSpan leaseTime));
        Assert.Equal(TimeSpan.FromHours(2), leaseTime);
    }

    [Fact]
    public async Task RequestSelecting_ForDifferentServerIdentifier_ReturnsNoReply()
    {
        await _host.SeedScopeAsync("other-server-scope", "192.0.2.60", "192.0.2.62");

        byte[] clientHardwareAddress = [0x44, 0x44, 0x44, 0x44, 0x44, 0x44];
        Dhcpv4Message request = NewMessage(clientHardwareAddress, 0x6000);
        request.SetMessageType(Dhcpv4MessageTypeEnum.Request);
        request.SetServerIdentifier(IPAddress.Parse("203.0.113.99"));
        request.SetRequestedIpAddress(IPAddress.Parse("192.0.2.60"));

        Dhcpv4Message reply = await _host.Handler.HandleRequestAsync(
            request,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.Null(reply);
    }

    [Fact]
    public async Task RequestInitReboot_ForAnAddressAlreadyHeldByAnotherClient_ReturnsNak()
    {
        DhcpScopeEntity scope = await _host.SeedScopeAsync(
            "init-reboot-scope",
            "192.0.2.70",
            "192.0.2.72"
        );

        await ConfirmAddressForNewClientAsync(scope, [0x55, 0x55, 0x55, 0x55, 0x55, 0x55]);

        // A different client claims (via INIT-REBOOT) the exact address the first client already
        // actively holds.
        byte[] otherClientHardwareAddress = [0x66, 0x66, 0x66, 0x66, 0x66, 0x66];
        Dhcpv4Message request = NewMessage(otherClientHardwareAddress, 0x7000);
        request.SetMessageType(Dhcpv4MessageTypeEnum.Request);
        request.SetRequestedIpAddress(IPAddress.Parse("192.0.2.70"));

        Dhcpv4Message reply = await _host.Handler.HandleRequestAsync(
            request,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(reply);
        Assert.True(reply.TryGetMessageType(out Dhcpv4MessageTypeEnum messageType));
        Assert.Equal(Dhcpv4MessageTypeEnum.Nak, messageType);
    }

    [Fact]
    public async Task Release_FreesTheAddressForAnotherClientImmediately()
    {
        DhcpScopeEntity scope = await _host.SeedScopeAsync(
            "release-scope",
            "192.0.2.80",
            "192.0.2.80"
        );
        byte[] clientHardwareAddress = [0x77, 0x77, 0x77, 0x77, 0x77, 0x77];
        LeaseEntity active = await ConfirmAddressForNewClientAsync(scope, clientHardwareAddress);

        Dhcpv4Message release = NewMessage(clientHardwareAddress, 0x8000);
        release.SetMessageType(Dhcpv4MessageTypeEnum.Release);
        release.ClientIpAddress = IPAddress.Parse(active.IpAddress);

        Dhcpv4Message reply = await _host.Handler.HandleRequestAsync(
            release,
            RemoteEndPoint,
            CancellationToken.None
        );
        Assert.Null(reply); // RFC 2131: no reply to a RELEASE.

        // The one-address pool is now free again for a brand-new client.
        Dhcpv4Message discover = NewMessage([0x88, 0x88, 0x88, 0x88, 0x88, 0x88], 0x9000);
        discover.SetMessageType(Dhcpv4MessageTypeEnum.Discover);
        Dhcpv4Message offer = await _host.Handler.HandleRequestAsync(
            discover,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(offer);
        Assert.Equal(IPAddress.Parse(active.IpAddress), offer.YourIpAddress);
    }

    [Fact]
    public async Task Decline_QuarantinesTheAddressForEveryClientUntilTheWindowElapses()
    {
        await _host.SeedScopeAsync("decline-scope", "192.0.2.90", "192.0.2.90");

        byte[] clientHardwareAddress = [0x99, 0x99, 0x99, 0x99, 0x99, 0x99];
        Dhcpv4Message discover = NewMessage(clientHardwareAddress, 0xA000);
        discover.SetMessageType(Dhcpv4MessageTypeEnum.Discover);
        Dhcpv4Message offer = await _host.Handler.HandleRequestAsync(
            discover,
            RemoteEndPoint,
            CancellationToken.None
        );
        Assert.NotNull(offer);

        Dhcpv4Message decline = NewMessage(clientHardwareAddress, 0xA000);
        decline.SetMessageType(Dhcpv4MessageTypeEnum.Decline);
        decline.SetRequestedIpAddress(offer.YourIpAddress);
        offer.TryGetServerIdentifier(out IPAddress serverIdentifier);
        decline.SetServerIdentifier(serverIdentifier);

        Dhcpv4Message declineReply = await _host.Handler.HandleRequestAsync(
            decline,
            RemoteEndPoint,
            CancellationToken.None
        );
        Assert.Null(declineReply); // RFC 2131: no reply to a DECLINE.

        // The pool's only address is now quarantined -- even the declining client itself gets no
        // new offer immediately afterward.
        Dhcpv4Message secondDiscover = NewMessage(clientHardwareAddress, 0xA001);
        secondDiscover.SetMessageType(Dhcpv4MessageTypeEnum.Discover);
        Dhcpv4Message secondOffer = await _host.Handler.HandleRequestAsync(
            secondDiscover,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.Null(secondOffer);
    }

    [Fact]
    public async Task Inform_RespondsWithOptionsButNoAddressOrLeaseTime()
    {
        await _host.SeedScopeAsync(
            "inform-scope",
            "192.0.2.100",
            "192.0.2.102",
            gatewayAddresses: ["192.0.2.1"]
        );

        Dhcpv4Message inform = NewMessage([0xbb, 0xbb, 0xbb, 0xbb, 0xbb, 0xbb], 0xB000);
        inform.SetMessageType(Dhcpv4MessageTypeEnum.Inform);
        inform.ClientIpAddress = IPAddress.Parse("192.0.2.101");

        Dhcpv4Message ack = await _host.Handler.HandleRequestAsync(
            inform,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(ack);
        Assert.True(ack.TryGetMessageType(out Dhcpv4MessageTypeEnum messageType));
        Assert.Equal(Dhcpv4MessageTypeEnum.Ack, messageType);
        Assert.Equal(IPAddress.Any, ack.YourIpAddress);
        Assert.False(ack.TryGetIpAddressLeaseTime(out _));
        Assert.True(ack.TryGetRouters(out IReadOnlyList<IPAddress> routers));
        Assert.Equal(IPAddress.Parse("192.0.2.1"), Assert.Single(routers));
    }

    /// <summary>
    ///     This method drives a real DISCOVER-then-REQUEST(selecting) exchange for a brand-new
    ///     client, returning its own now-active lease -- a small, shared setup step several tests
    ///     above use to put an address genuinely in use before testing a second scenario against
    ///     it.
    /// </summary>
    private async Task<LeaseEntity> ConfirmAddressForNewClientAsync(
        DhcpScopeEntity scope,
        byte[] clientHardwareAddress
    )
    {
        Dhcpv4Message discover = NewMessage(clientHardwareAddress, 0xF000);
        discover.SetMessageType(Dhcpv4MessageTypeEnum.Discover);
        Dhcpv4Message offer = await _host.Handler.HandleRequestAsync(
            discover,
            RemoteEndPoint,
            CancellationToken.None
        );
        offer.TryGetServerIdentifier(out IPAddress serverIdentifier);

        Dhcpv4Message request = NewMessage(clientHardwareAddress, 0xF000);
        request.SetMessageType(Dhcpv4MessageTypeEnum.Request);
        request.SetServerIdentifier(serverIdentifier);
        request.SetRequestedIpAddress(offer.YourIpAddress);

        await _host.Handler.HandleRequestAsync(request, RemoteEndPoint, CancellationToken.None);

        string clientKey = string.Join(':', clientHardwareAddress.Select(b => b.ToString("x2")));
        return await _host.FindLeaseAsync(scope.Name, clientKey);
    }

    /// <summary>
    ///     This method builds the common shape every test message in this class shares.
    /// </summary>
    private static Dhcpv4Message NewMessage(byte[] clientHardwareAddress, uint transactionId) =>
        new()
        {
            OperationCode = Dhcpv4OperationCodeEnum.BootRequest,
            TransactionId = transactionId,
            HardwareAddressLength = (byte)clientHardwareAddress.Length,
            ClientHardwareAddress = clientHardwareAddress,
        };
}
