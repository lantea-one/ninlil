using System.Net;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Net;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Tests;

/// <summary>
///     This test class covers the DHCPv4 engine's message handling (DISCOVER, the REQUEST cases,
///     NAKs, lease times, reservations, RELEASE, DECLINE, INFORM) by calling
///     <see cref="Dhcpv4RequestHandler.HandleRequestAsync" /> directly, without a socket (see
///     <see cref="Dhcpv4UdpRoundTripTests" /> for that), on a <see cref="Dhcpv4EngineTestHost" />
///     with the Entity Framework Core repositories and a temporary SQLite file, so every change is
///     saved and read back.
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

        // First give the pool's only address to another client.
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

    /// <summary>
    ///     This test pins the contract between the web API and the engine: a reservation stored
    ///     with the identifier <see cref="ClientIdentifierFormat.TryNormalize" /> produces for a
    ///     hyphenated uppercase MAC is honored for that client's DISCOVER.
    /// </summary>
    [Fact]
    public async Task Discover_ForAReservationStoredInTheNormalizedForm_OffersTheReservedAddress()
    {
        DhcpScopeEntity scope = await _host.SeedScopeAsync(
            "normalized-reservation-scope",
            "192.0.2.50",
            "192.0.2.52"
        );
        Assert.True(ClientIdentifierFormat.TryNormalize("AA-BB-CC-DD-EE-04", out string stored));
        await _host.SeedReservationAsync(scope.Id, stored, "192.0.2.98");

        Dhcpv4Message discover = NewMessage([0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0x04], 0x3100);
        discover.SetMessageType(Dhcpv4MessageTypeEnum.Discover);

        Dhcpv4Message offer = await _host.Handler.HandleRequestAsync(
            discover,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(offer);
        Assert.Equal(IPAddress.Parse("192.0.2.98"), offer.YourIpAddress);
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

        // The client asks for less than the scope default; the server grants what was asked
        // (Dhcpv4LeaseTimeNegotiator).
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

        // Another client claims, by INIT-REBOOT, the address the first client holds.
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

        // The one-address pool is free again for a new client.
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

        // The pool's only address is quarantined, so even the declining client gets no offer
        // (there is no other free address to move its row to).
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
    public async Task Decline_FromAnotherClient_LeavesTheOwnersLeaseAlone()
    {
        DhcpScopeEntity scope = await _host.SeedScopeAsync(
            "decline-foreign",
            "192.0.2.110",
            "192.0.2.111"
        );
        byte[] ownerHardwareAddress = [0x10, 0x10, 0x10, 0x10, 0x10, 0x10];
        LeaseEntity active = await ConfirmAddressForNewClientAsync(scope, ownerHardwareAddress);

        Dhcpv4Message decline = NewDecline(
            [0x20, 0x20, 0x20, 0x20, 0x20, 0x20],
            IPAddress.Parse(active.IpAddress),
            IPAddress.Parse(Dhcpv4EngineTestHost.ServerIdentifierAddress)
        );
        Assert.Null(
            await _host.Handler.HandleRequestAsync(decline, RemoteEndPoint, CancellationToken.None)
        );

        LeaseEntity after = await _host.FindLeaseAsync(scope.Name, "10:10:10:10:10:10");
        Assert.Equal(LeaseStateEnum.Active, after.State);
        Assert.Equal(active.LeaseExpiresAtUtc, after.LeaseExpiresAtUtc);
        Assert.Null(await _host.FindLeaseAsync(scope.Name, "20:20:20:20:20:20"));

        // The owner's next REQUEST is still ACKed.
        Dhcpv4Message renew = NewMessage(ownerHardwareAddress, 0xC001);
        renew.SetMessageType(Dhcpv4MessageTypeEnum.Request);
        renew.ClientIpAddress = IPAddress.Parse(active.IpAddress);
        Dhcpv4Message ack = await _host.Handler.HandleRequestAsync(
            renew,
            RemoteEndPoint,
            CancellationToken.None
        );
        Assert.NotNull(ack);
        Assert.True(ack.TryGetMessageType(out Dhcpv4MessageTypeEnum type));
        Assert.Equal(Dhcpv4MessageTypeEnum.Ack, type);
    }

    [Fact]
    public async Task Decline_OfAFreeAddress_CreatesNoRowAndTheAddressStaysAvailable()
    {
        DhcpScopeEntity scope = await _host.SeedScopeAsync(
            "decline-free",
            "192.0.2.120",
            "192.0.2.120"
        );

        Dhcpv4Message decline = NewDecline(
            [0x30, 0x30, 0x30, 0x30, 0x30, 0x30],
            IPAddress.Parse("192.0.2.120"),
            IPAddress.Parse(Dhcpv4EngineTestHost.ServerIdentifierAddress)
        );
        await _host.Handler.HandleRequestAsync(decline, RemoteEndPoint, CancellationToken.None);

        Assert.Null(await _host.FindLeaseAsync(scope.Name, "30:30:30:30:30:30"));

        // The one-address pool is still offered to another client.
        Dhcpv4Message discover = NewMessage([0x31, 0x31, 0x31, 0x31, 0x31, 0x31], 0xC100);
        discover.SetMessageType(Dhcpv4MessageTypeEnum.Discover);
        Dhcpv4Message offer = await _host.Handler.HandleRequestAsync(
            discover,
            RemoteEndPoint,
            CancellationToken.None
        );
        Assert.NotNull(offer);
        Assert.Equal(IPAddress.Parse("192.0.2.120"), offer.YourIpAddress);
    }

    [Fact]
    public async Task Decline_NamingAnotherServer_IsIgnored()
    {
        DhcpScopeEntity scope = await _host.SeedScopeAsync(
            "decline-other-server",
            "192.0.2.130",
            "192.0.2.130"
        );
        byte[] hardwareAddress = [0x40, 0x40, 0x40, 0x40, 0x40, 0x40];
        LeaseEntity active = await ConfirmAddressForNewClientAsync(scope, hardwareAddress);

        Dhcpv4Message decline = NewDecline(
            hardwareAddress,
            IPAddress.Parse(active.IpAddress),
            IPAddress.Parse("192.0.2.200")
        );
        await _host.Handler.HandleRequestAsync(decline, RemoteEndPoint, CancellationToken.None);

        LeaseEntity after = await _host.FindLeaseAsync(scope.Name, "40:40:40:40:40:40");
        Assert.Equal(LeaseStateEnum.Active, after.State);
        Assert.Equal(active.LeaseExpiresAtUtc, after.LeaseExpiresAtUtc);
    }

    [Fact]
    public async Task Decline_FromTheOwnerWithoutAServerIdentifier_StillQuarantines()
    {
        DhcpScopeEntity scope = await _host.SeedScopeAsync(
            "decline-no-sid",
            "192.0.2.140",
            "192.0.2.140"
        );
        byte[] hardwareAddress = [0x50, 0x50, 0x50, 0x50, 0x50, 0x50];
        LeaseEntity active = await ConfirmAddressForNewClientAsync(scope, hardwareAddress);

        Dhcpv4Message decline = NewDecline(
            hardwareAddress,
            IPAddress.Parse(active.IpAddress),
            serverIdentifier: null
        );
        await _host.Handler.HandleRequestAsync(decline, RemoteEndPoint, CancellationToken.None);

        // The row is re-keyed to the address, so the client holds no row and nobody owns the address.
        Assert.Null(await _host.FindLeaseAsync(scope.Name, "50:50:50:50:50:50"));
        LeaseEntity after = await _host.FindLeaseAsync(scope.Name, $"declined:{active.IpAddress}");
        Assert.Equal(LeaseStateEnum.Expired, after.State);
        Assert.True(after.LeaseExpiresAtUtc > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Release_NamingAnotherServer_LeavesTheLeaseUnchanged()
    {
        DhcpScopeEntity scope = await _host.SeedScopeAsync(
            "release-other-server",
            "192.0.2.150",
            "192.0.2.150"
        );
        byte[] hardwareAddress = [0x60, 0x60, 0x60, 0x60, 0x60, 0x60];
        LeaseEntity active = await ConfirmAddressForNewClientAsync(scope, hardwareAddress);

        Dhcpv4Message release = NewMessage(hardwareAddress, 0xC200);
        release.SetMessageType(Dhcpv4MessageTypeEnum.Release);
        release.ClientIpAddress = IPAddress.Parse(active.IpAddress);
        release.SetServerIdentifier(IPAddress.Parse("192.0.2.200"));
        await _host.Handler.HandleRequestAsync(release, RemoteEndPoint, CancellationToken.None);

        LeaseEntity after = await _host.FindLeaseAsync(scope.Name, "60:60:60:60:60:60");
        Assert.Equal(LeaseStateEnum.Active, after.State);
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
    ///     This method runs a DISCOVER and a selecting REQUEST for a new client and returns its
    ///     active lease, so tests can start with an address in use.
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
    ///     This method builds a DECLINE of an address, with a Server Identifier unless
    ///     <paramref name="serverIdentifier" /> is <see langword="null" />.
    /// </summary>
    private static Dhcpv4Message NewDecline(
        byte[] clientHardwareAddress,
        IPAddress declinedAddress,
        IPAddress serverIdentifier
    )
    {
        Dhcpv4Message decline = NewMessage(clientHardwareAddress, 0xC000);
        decline.SetMessageType(Dhcpv4MessageTypeEnum.Decline);
        decline.SetRequestedIpAddress(declinedAddress);

        if (serverIdentifier is not null)
            decline.SetServerIdentifier(serverIdentifier);

        return decline;
    }

    /// <summary>
    ///     This method builds a request message with the fields every test sets.
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
