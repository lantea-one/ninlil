using System.Net;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Tests;

/// <summary>
///     This test class checks where the DHCPv4 engine publishes <see cref="DhcpLeaseChangedEvent" />:
///     for a granted lease, a release and a decline (an Expired change), never for an offer (see
///     <see cref="Ninlil.Dhcp.Server.Core.Allocation.Dhcpv4LeaseAllocationService" />). It reads
///     the recording publisher of a <see cref="Dhcpv4EngineTestHost" />; delivery to subscribers
///     through the event bus is tested in <c>Ninlil.Dhcp.Server.DnsIntegration.Tests</c>.
/// </summary>
public sealed class Dhcpv4LeaseChangedEventPublishingTests : IAsyncLifetime
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
    public async Task Discover_PublishesNoEvent()
    {
        await _host.SeedScopeAsync("event-discover-scope", "192.0.2.100", "192.0.2.102");

        Dhcpv4Message discover = NewMessage([0x01, 0x01, 0x01, 0x01, 0x01, 0x01], 0x1000);
        discover.SetMessageType(Dhcpv4MessageTypeEnum.Discover);

        await _host.Handler.HandleRequestAsync(discover, RemoteEndPoint, CancellationToken.None);

        Assert.Empty(_host.PublishedEvents);
    }

    [Fact]
    public async Task RequestConfirmingAnOffer_PublishesGranted()
    {
        await _host.SeedScopeAsync("event-grant-scope", "192.0.2.110", "192.0.2.112");
        byte[] clientHardwareAddress = [0x02, 0x02, 0x02, 0x02, 0x02, 0x02];

        Dhcpv4Message discover = NewMessage(clientHardwareAddress, 0x2000);
        discover.SetMessageType(Dhcpv4MessageTypeEnum.Discover);
        Dhcpv4Message offer = await _host.Handler.HandleRequestAsync(
            discover,
            RemoteEndPoint,
            CancellationToken.None
        );
        offer.TryGetServerIdentifier(out IPAddress serverIdentifier);

        Assert.Empty(_host.PublishedEvents);

        Dhcpv4Message request = NewMessage(clientHardwareAddress, 0x2000);
        request.SetMessageType(Dhcpv4MessageTypeEnum.Request);
        request.SetServerIdentifier(serverIdentifier);
        request.SetRequestedIpAddress(offer.YourIpAddress);

        await _host.Handler.HandleRequestAsync(request, RemoteEndPoint, CancellationToken.None);

        DhcpLeaseChangedEvent published = Assert.IsType<DhcpLeaseChangedEvent>(
            Assert.Single(_host.PublishedEvents)
        );
        Assert.Equal(DhcpLeaseChangeKindEnum.Granted, published.ChangeKind);

        LeaseEntity lease = await _host.FindLeaseAsync("event-grant-scope", "02:02:02:02:02:02");
        Assert.Equal(lease.Id, published.LeaseId);
    }

    [Fact]
    public async Task Release_PublishesReleased()
    {
        DhcpScopeEntity scope = await _host.SeedScopeAsync(
            "event-release-scope",
            "192.0.2.120",
            "192.0.2.120"
        );
        byte[] clientHardwareAddress = [0x03, 0x03, 0x03, 0x03, 0x03, 0x03];

        Dhcpv4Message discover = NewMessage(clientHardwareAddress, 0x3000);
        discover.SetMessageType(Dhcpv4MessageTypeEnum.Discover);
        Dhcpv4Message offer = await _host.Handler.HandleRequestAsync(
            discover,
            RemoteEndPoint,
            CancellationToken.None
        );
        offer.TryGetServerIdentifier(out IPAddress serverIdentifier);

        Dhcpv4Message request = NewMessage(clientHardwareAddress, 0x3000);
        request.SetMessageType(Dhcpv4MessageTypeEnum.Request);
        request.SetServerIdentifier(serverIdentifier);
        request.SetRequestedIpAddress(offer.YourIpAddress);
        await _host.Handler.HandleRequestAsync(request, RemoteEndPoint, CancellationToken.None);

        _host.PublishedEvents.Clear();

        Dhcpv4Message release = NewMessage(clientHardwareAddress, 0x3001);
        release.SetMessageType(Dhcpv4MessageTypeEnum.Release);
        release.ClientIpAddress = offer.YourIpAddress;

        await _host.Handler.HandleRequestAsync(release, RemoteEndPoint, CancellationToken.None);

        DhcpLeaseChangedEvent published = Assert.IsType<DhcpLeaseChangedEvent>(
            Assert.Single(_host.PublishedEvents)
        );
        Assert.Equal(DhcpLeaseChangeKindEnum.Released, published.ChangeKind);
    }

    [Fact]
    public async Task Decline_PublishesExpired()
    {
        DhcpScopeEntity scope = await _host.SeedScopeAsync(
            "event-decline-scope",
            "192.0.2.130",
            "192.0.2.130"
        );
        byte[] clientHardwareAddress = [0x04, 0x04, 0x04, 0x04, 0x04, 0x04];

        Dhcpv4Message discover = NewMessage(clientHardwareAddress, 0x4000);
        discover.SetMessageType(Dhcpv4MessageTypeEnum.Discover);
        Dhcpv4Message offer = await _host.Handler.HandleRequestAsync(
            discover,
            RemoteEndPoint,
            CancellationToken.None
        );

        _host.PublishedEvents.Clear();

        Dhcpv4Message decline = NewMessage(clientHardwareAddress, 0x4000);
        decline.SetMessageType(Dhcpv4MessageTypeEnum.Decline);
        decline.SetRequestedIpAddress(offer.YourIpAddress);

        await _host.Handler.HandleRequestAsync(decline, RemoteEndPoint, CancellationToken.None);

        DhcpLeaseChangedEvent published = Assert.IsType<DhcpLeaseChangedEvent>(
            Assert.Single(_host.PublishedEvents)
        );
        Assert.Equal(DhcpLeaseChangeKindEnum.Expired, published.ChangeKind);
    }

    private static Dhcpv4Message NewMessage(byte[] clientHardwareAddress, uint transactionId) =>
        new()
        {
            OperationCode = Dhcpv4OperationCodeEnum.BootRequest,
            TransactionId = transactionId,
            HardwareAddressLength = (byte)clientHardwareAddress.Length,
            ClientHardwareAddress = clientHardwareAddress,
        };
}
