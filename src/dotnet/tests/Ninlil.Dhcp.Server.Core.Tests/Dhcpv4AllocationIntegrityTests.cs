using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Tests;

/// <summary>
///     This test class covers the DHCPv4 allocation rules that keep lease rows consistent: a
///     declined address stays quarantined under a <c>declined:&lt;address&gt;</c> row, another
///     client's stale row never makes an OFFER fail on the unique index, and a reservation
///     outranks the client's current binding. It sends real messages through
///     <see cref="Dhcpv4RequestHandler" /> on a <see cref="Dhcpv4EngineTestHost" /> (temporary
///     SQLite file with the unique indexes enforced) and seeds special rows through the
///     repositories.
/// </summary>
public sealed class Dhcpv4AllocationIntegrityTests : IAsyncLifetime
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
    public async Task Decline_KeepsTheAddressQuarantinedForEveryClientAndNaksTheDecliner()
    {
        DhcpScopeEntity scope = await _host.SeedScopeAsync(
            "integrity-decline",
            "192.0.2.10",
            "192.0.2.12"
        );
        byte[] decliner = [0xa1, 0, 0, 0, 0, 1];
        Dhcpv4Message offer = await DiscoverAsync(decliner);
        Assert.Equal(IPAddress.Parse("192.0.2.10"), offer.YourIpAddress);
        await RequestSelectingAsync(decliner, offer);
        _host.PublishedEvents.Clear();

        await SendAsync(DeclineMessage(decliner, offer.YourIpAddress));

        // One Expired event for the declined row.
        DhcpLeaseChangedEvent changed = Assert.IsType<DhcpLeaseChangedEvent>(
            Assert.Single(_host.PublishedEvents)
        );
        Assert.Equal(DhcpLeaseChangeKindEnum.Expired, changed.ChangeKind);

        // The next DISCOVER of the decliner is not offered the declined address.
        Dhcpv4Message again = await DiscoverAsync(decliner);
        Assert.NotNull(again);
        Assert.NotEqual(offer.YourIpAddress, again.YourIpAddress);

        // The quarantine row outlives the decliner's move to a new address.
        LeaseEntity quarantine = await _host.FindLeaseAsync(scope.Name, "declined:192.0.2.10");
        Assert.NotNull(quarantine);
        Assert.Equal(LeaseStateEnum.Expired, quarantine.State);
        Assert.True(quarantine.LeaseExpiresAtUtc > DateTimeOffset.UtcNow);

        // Another client asking for the declined address does not get it.
        Dhcpv4Message other = NewMessage([0xa1, 0, 0, 0, 0, 2], 0x10);
        other.SetMessageType(Dhcpv4MessageTypeEnum.Discover);
        other.SetRequestedIpAddress(offer.YourIpAddress);
        Dhcpv4Message otherOffer = await SendAsync(other);
        Assert.NotNull(otherOffer);
        Assert.NotEqual(offer.YourIpAddress, otherOffer.YourIpAddress);

        // The decliner cannot be ACKed the address it declined.
        Dhcpv4Message request = NewMessage(decliner, 0x11);
        request.SetMessageType(Dhcpv4MessageTypeEnum.Request);
        request.SetRequestedIpAddress(offer.YourIpAddress);
        Dhcpv4Message reply = await SendAsync(request);
        Assert.NotNull(reply);
        Assert.True(reply.TryGetMessageType(out Dhcpv4MessageTypeEnum type));
        Assert.Equal(Dhcpv4MessageTypeEnum.Nak, type);
    }

    [Fact]
    public async Task Discover_ForAClientWhoseRowIsMovingOntoAnotherClientsStaleRow_Offers()
    {
        DhcpScopeEntity scope = await _host.SeedScopeAsync(
            "integrity-stale",
            "192.0.2.20",
            "192.0.2.25"
        );
        string clientA = "a2:00:00:00:00:01";
        string clientB = "a2:00:00:00:00:02";
        await SeedLeaseAsync(scope, clientA, "192.0.2.20", LeaseStateEnum.Released, TimeSpan.Zero);

        // B holds .21, which an operator has since reserved for C, so B cannot keep it.
        await SeedLeaseAsync(
            scope,
            clientB,
            "192.0.2.21",
            LeaseStateEnum.Active,
            TimeSpan.FromHours(1)
        );
        await _host.SeedReservationAsync(scope.Id, "a2:00:00:00:00:03", "192.0.2.21");

        Dhcpv4Message discover = NewMessage([0xa2, 0, 0, 0, 0, 2], 0x20);
        discover.SetMessageType(Dhcpv4MessageTypeEnum.Discover);
        discover.SetRequestedIpAddress(IPAddress.Parse("192.0.2.20"));
        Dhcpv4Message offer = await SendAsync(discover);

        Assert.NotNull(offer);
        Assert.Equal(IPAddress.Parse("192.0.2.20"), offer.YourIpAddress);
        Assert.Null(await _host.FindLeaseAsync(scope.Name, clientA));
        LeaseEntity b = await _host.FindLeaseAsync(scope.Name, clientB);
        Assert.Equal("192.0.2.20", b.IpAddress);
        Assert.Equal(LeaseStateEnum.Offered, b.State);
    }

    /// <summary>
    ///     This test checks that a stored reservation holding an IPv6 address (the API refuses
    ///     one, but a restore or a database edit can store it) is skipped with a Warning: the
    ///     device gets a pool address and an ACK instead of an encoding failure and no reply.
    /// </summary>
    [Fact]
    public async Task Discover_ForAReservationHoldingAnIPv6Address_SkipsItWithAWarningAndOffersAPoolAddress()
    {
        DhcpScopeEntity scope = await _host.SeedScopeAsync(
            "integrity-wrong-family",
            "192.0.2.60",
            "192.0.2.65"
        );
        await _host.SeedReservationAsync(scope.Id, "a5:00:00:00:00:01", "2001:db8::1");
        byte[] client = [0xa5, 0, 0, 0, 0, 1];

        Dhcpv4Message offer = await DiscoverAsync(client);

        Assert.NotNull(offer);
        Assert.Equal(IPAddress.Parse("192.0.2.60"), offer.YourIpAddress);
        Dhcpv4Message ack = await RequestSelectingAsync(client, offer);
        Assert.NotNull(ack);
        Assert.True(ack.TryGetMessageType(out Dhcpv4MessageTypeEnum type));
        Assert.Equal(Dhcpv4MessageTypeEnum.Ack, type);
        Assert.Contains(
            _host.Logs.Warnings,
            warning => warning.Contains("2001:db8::1") && warning.Contains("is not an IPv4 address")
        );
    }

    [Fact]
    public async Task Discover_ForAClientWithALeaseAndANewReservation_OffersAndAcksTheReservation()
    {
        DhcpScopeEntity scope = await _host.SeedScopeAsync(
            "integrity-reserve",
            "192.0.2.30",
            "192.0.2.45"
        );
        byte[] client = [0xa3, 0, 0, 0, 0, 1];
        Dhcpv4Message first = await DiscoverAsync(client);
        await RequestSelectingAsync(client, first);
        Assert.Equal(IPAddress.Parse("192.0.2.30"), first.YourIpAddress);

        await _host.SeedReservationAsync(scope.Id, "a3:00:00:00:00:01", "192.0.2.40");

        Dhcpv4Message offer = await DiscoverAsync(client);
        Assert.Equal(IPAddress.Parse("192.0.2.40"), offer.YourIpAddress);

        Dhcpv4Message ack = await RequestSelectingAsync(client, offer);
        Assert.True(ack.TryGetMessageType(out Dhcpv4MessageTypeEnum type));
        Assert.Equal(Dhcpv4MessageTypeEnum.Ack, type);

        LeaseEntity lease = await _host.FindLeaseAsync(scope.Name, "a3:00:00:00:00:01");
        Assert.Equal("192.0.2.40", lease.IpAddress);
        Assert.Equal(LeaseStateEnum.Active, lease.State);
    }

    [Fact]
    public async Task Discover_ForAClientWhoseAddressWasReservedForAnotherClient_OffersAnotherAddress()
    {
        DhcpScopeEntity scope = await _host.SeedScopeAsync(
            "integrity-taken",
            "192.0.2.50",
            "192.0.2.55"
        );
        byte[] client = [0xa4, 0, 0, 0, 0, 1];
        Dhcpv4Message first = await DiscoverAsync(client);
        await RequestSelectingAsync(client, first);
        Assert.Equal(IPAddress.Parse("192.0.2.50"), first.YourIpAddress);

        await _host.SeedReservationAsync(scope.Id, "a4:00:00:00:00:09", "192.0.2.50");

        Dhcpv4Message offer = await DiscoverAsync(client);
        Assert.NotNull(offer);
        Assert.NotEqual(IPAddress.Parse("192.0.2.50"), offer.YourIpAddress);

        Dhcpv4Message ack = await RequestSelectingAsync(client, offer);
        Assert.True(ack.TryGetMessageType(out Dhcpv4MessageTypeEnum type));
        Assert.Equal(Dhcpv4MessageTypeEnum.Ack, type);
    }

    [Fact]
    public async Task Discover_WithAQuarantineStoredOnTheClientsOwnRow_MovesItAndOffersAnotherAddress()
    {
        DhcpScopeEntity scope = await _host.SeedScopeAsync(
            "integrity-legacy",
            "192.0.2.60",
            "192.0.2.65"
        );
        await SeedLeaseAsync(
            scope,
            "a5:00:00:00:00:01",
            "192.0.2.60",
            LeaseStateEnum.Expired,
            TimeSpan.FromMinutes(5)
        );

        Dhcpv4Message offer = await DiscoverAsync([0xa5, 0, 0, 0, 0, 1]);

        Assert.NotNull(offer);
        Assert.NotEqual(IPAddress.Parse("192.0.2.60"), offer.YourIpAddress);
        LeaseEntity quarantine = await _host.FindLeaseAsync(scope.Name, "declined:192.0.2.60");
        Assert.NotNull(quarantine);
        Assert.True(quarantine.LeaseExpiresAtUtc > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Discover_ForAClientWhoseLeaseExpiredNaturally_OffersItsPreviousAddress()
    {
        DhcpScopeEntity scope = await _host.SeedScopeAsync(
            "integrity-previous",
            "192.0.2.69",
            "192.0.2.75"
        );
        await SeedLeaseAsync(
            scope,
            "a6:00:00:00:00:01",
            "192.0.2.70",
            LeaseStateEnum.Expired,
            TimeSpan.FromMinutes(-5)
        );

        Dhcpv4Message offer = await DiscoverAsync([0xa6, 0, 0, 0, 0, 1]);

        Assert.NotNull(offer);
        Assert.Equal(IPAddress.Parse("192.0.2.70"), offer.YourIpAddress);
    }

    /// <summary>
    ///     This method saves a lease row through the repository with an expiry relative to now.
    /// </summary>
    private async Task SeedLeaseAsync(
        DhcpScopeEntity scope,
        string clientIdentifier,
        string ipAddress,
        LeaseStateEnum state,
        TimeSpan expiresIn
    )
    {
        await using AsyncServiceScope diScope = _host.Host.Services.CreateAsyncScope();
        await diScope
            .ServiceProvider.GetRequiredService<ILeaseRepository>()
            .AddAsync(
                new LeaseEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ProtocolVersion = InternetProtocolVersionEnum.IPv4,
                    ScopeName = scope.Name,
                    ClientIdentifier = clientIdentifier,
                    IpAddress = ipAddress,
                    State = state,
                    LeaseExpiresAtUtc = DateTimeOffset.UtcNow + expiresIn,
                }
            );
        await diScope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    /// <summary>
    ///     This method sends a DISCOVER from a client and returns the reply.
    /// </summary>
    private async Task<Dhcpv4Message> DiscoverAsync(byte[] client)
    {
        Dhcpv4Message discover = NewMessage(client, 0x1);
        discover.SetMessageType(Dhcpv4MessageTypeEnum.Discover);
        return await SendAsync(discover);
    }

    /// <summary>
    ///     This method sends the selecting REQUEST that answers an OFFER and returns the reply.
    /// </summary>
    private async Task<Dhcpv4Message> RequestSelectingAsync(byte[] client, Dhcpv4Message offer)
    {
        offer.TryGetServerIdentifier(out IPAddress serverIdentifier);
        Dhcpv4Message request = NewMessage(client, 0x2);
        request.SetMessageType(Dhcpv4MessageTypeEnum.Request);
        request.SetServerIdentifier(serverIdentifier);
        request.SetRequestedIpAddress(offer.YourIpAddress);
        return await SendAsync(request);
    }

    private static Dhcpv4Message DeclineMessage(byte[] client, IPAddress address)
    {
        Dhcpv4Message decline = NewMessage(client, 0x3);
        decline.SetMessageType(Dhcpv4MessageTypeEnum.Decline);
        decline.SetRequestedIpAddress(address);
        decline.SetServerIdentifier(IPAddress.Parse(Dhcpv4EngineTestHost.ServerIdentifierAddress));
        return decline;
    }

    private ValueTask<Dhcpv4Message> SendAsync(Dhcpv4Message message) =>
        _host.Handler.HandleRequestAsync(message, RemoteEndPoint, CancellationToken.None);

    private static Dhcpv4Message NewMessage(byte[] clientHardwareAddress, uint transactionId) =>
        new()
        {
            OperationCode = Dhcpv4OperationCodeEnum.BootRequest,
            TransactionId = transactionId,
            HardwareAddressLength = (byte)clientHardwareAddress.Length,
            ClientHardwareAddress = clientHardwareAddress,
        };
}
