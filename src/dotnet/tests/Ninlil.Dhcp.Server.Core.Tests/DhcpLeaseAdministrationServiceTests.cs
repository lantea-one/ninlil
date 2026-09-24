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
///     This test proves <see cref="Administration.IDhcpLeaseAdministrationService.ForceReleaseAsync" />
///     against a real, granted lease -- the real DHCPv4 allocation engine grants it through a
///     genuine DISCOVER/REQUEST exchange (mirroring <see cref="Dhcpv4RequestHandlerTests" />), this
///     service force-releases it, and a fresh DISCOVER from a different client proves the address
///     is genuinely available for reallocation again, not merely that the lease row's own
///     <c>State</c> column changed.
/// </summary>
public sealed class DhcpLeaseAdministrationServiceTests : IAsyncLifetime
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
    public async Task ForceReleaseAsync_ForARealActiveLease_FreesTheAddressForReallocation()
    {
        await _host.SeedScopeAsync("force-release-scope", "192.0.2.200", "192.0.2.200");

        byte[] firstClient = [0xaa, 0xaa, 0xaa, 0xaa, 0xaa, 0xaa];
        Dhcpv4Message discover = NewMessage(firstClient, 0x1000);
        discover.SetMessageType(Dhcpv4MessageTypeEnum.Discover);
        Dhcpv4Message offer = await _host.Handler.HandleRequestAsync(
            discover,
            RemoteEndPoint,
            CancellationToken.None
        );
        Assert.NotNull(offer);

        Dhcpv4Message request = NewMessage(firstClient, 0x1000);
        request.SetMessageType(Dhcpv4MessageTypeEnum.Request);
        request.SetRequestedIpAddress(offer.YourIpAddress);
        Dhcpv4Message ack = await _host.Handler.HandleRequestAsync(
            request,
            RemoteEndPoint,
            CancellationToken.None
        );
        Assert.NotNull(ack);

        LeaseEntity grantedLease = await _host.FindLeaseAsync(
            "force-release-scope",
            "aa:aa:aa:aa:aa:aa"
        );
        Assert.NotNull(grantedLease);
        Assert.Equal(LeaseStateEnum.Active, grantedLease.State);

        // The scope's only address is fully occupied -- a second client gets no offer at all.
        byte[] secondClient = [0xbb, 0xbb, 0xbb, 0xbb, 0xbb, 0xbb];
        Dhcpv4Message discoverBeforeRelease = NewMessage(secondClient, 0x1001);
        discoverBeforeRelease.SetMessageType(Dhcpv4MessageTypeEnum.Discover);
        Assert.Null(
            await _host.Handler.HandleRequestAsync(
                discoverBeforeRelease,
                RemoteEndPoint,
                CancellationToken.None
            )
        );

        _host.PublishedEvents.Clear();

        LeaseEntity released = await _host.ForceReleaseLeaseAsync(grantedLease.Id);
        Assert.NotNull(released);
        Assert.Equal(LeaseStateEnum.Released, released.State);

        // The exact same DhcpLeaseChangedEvent(Released) a real client-initiated release already
        // publishes -- proving DNS auto-registration cleanup (which subscribes to this event) is
        // triggered identically.
        DhcpLeaseChangedEvent publishedEvent =
            Assert.Single(_host.PublishedEvents) as DhcpLeaseChangedEvent;
        Assert.NotNull(publishedEvent);
        Assert.Equal(grantedLease.Id, publishedEvent.LeaseId);
        Assert.Equal(DhcpLeaseChangeKindEnum.Released, publishedEvent.ChangeKind);

        // The genuine proof: a second client's own DISCOVER now succeeds against the real
        // allocation engine, offered the exact same address.
        Dhcpv4Message discoverAfterRelease = NewMessage(secondClient, 0x1002);
        discoverAfterRelease.SetMessageType(Dhcpv4MessageTypeEnum.Discover);
        Dhcpv4Message offerAfterRelease = await _host.Handler.HandleRequestAsync(
            discoverAfterRelease,
            RemoteEndPoint,
            CancellationToken.None
        );
        Assert.NotNull(offerAfterRelease);
        Assert.Equal(IPAddress.Parse("192.0.2.200"), offerAfterRelease.YourIpAddress);
    }

    [Fact]
    public async Task ForceReleaseAsync_ForAnAlreadyReleasedLease_IsIdempotentAndPublishesNoEvent()
    {
        DhcpScopeEntity scope = await _host.SeedScopeAsync(
            "force-release-idempotent-scope",
            "192.0.2.210",
            "192.0.2.210"
        );

        LeaseEntity lease = await _host.ForceReleaseLeaseAsync(Guid.NewGuid());
        Assert.Null(lease);

        // Seed an already-released lease directly, then force-release it twice -- neither call
        // should publish a second event.
        Guid leaseId = Guid.NewGuid();
        await using (AsyncServiceScope seedScope = _host.Host.Services.CreateAsyncScope())
        {
            ILeaseRepository leaseRepository =
                seedScope.ServiceProvider.GetRequiredService<ILeaseRepository>();
            IUnitOfWork unitOfWork = seedScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await leaseRepository.AddAsync(
                new LeaseEntity
                {
                    Id = leaseId,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ProtocolVersion = InternetProtocolVersionEnum.IPv4,
                    ScopeName = scope.Name,
                    ClientIdentifier = "cc:cc:cc:cc:cc:cc",
                    IpAddress = "192.0.2.210",
                    State = LeaseStateEnum.Released,
                    LeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
                }
            );
            await unitOfWork.SaveChangesAsync();
        }

        _host.PublishedEvents.Clear();

        LeaseEntity firstCall = await _host.ForceReleaseLeaseAsync(leaseId);
        LeaseEntity secondCall = await _host.ForceReleaseLeaseAsync(leaseId);

        Assert.NotNull(firstCall);
        Assert.NotNull(secondCall);
        Assert.Equal(LeaseStateEnum.Released, firstCall.State);
        Assert.Equal(LeaseStateEnum.Released, secondCall.State);
        Assert.Empty(_host.PublishedEvents);
    }

    /// <summary>
    ///     This method builds the common message shape every test in this class shares -- mirrors
    ///     <see cref="Dhcpv4RequestHandlerTests" />'s own helper exactly.
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
