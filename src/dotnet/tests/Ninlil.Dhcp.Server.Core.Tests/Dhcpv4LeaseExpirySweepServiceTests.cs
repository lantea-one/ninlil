using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Tests;

/// <summary>
///     This test proves <see cref="Dhcpv4LeaseExpirySweepService" /> -- the one mechanism that
///     ever detects a lease's own natural expiry -- against a real, SQLite-backed
///     <see cref="Dhcpv4EngineTestHost" />.
/// </summary>
public sealed class Dhcpv4LeaseExpirySweepServiceTests : IAsyncLifetime
{
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
    public async Task SweepAsync_ExpiresAnOverdueActiveLease_AndPublishesExpired()
    {
        DhcpScopeEntity scope = await _host.SeedScopeAsync(
            "sweep-scope",
            "192.0.2.140",
            "192.0.2.140"
        );
        LeaseEntity lease = await SeedActiveLeaseAsync(
            scope.Name,
            "05:05:05:05:05:05",
            "192.0.2.140",
            DateTimeOffset.UtcNow.AddMinutes(-1)
        );

        Dhcpv4LeaseExpirySweepService sweepService = GetSweepService();

        int expiredCount = await sweepService.SweepAsync();

        Assert.Equal(1, expiredCount);

        LeaseEntity reloaded = await _host.FindLeaseAsync(scope.Name, "05:05:05:05:05:05");
        Assert.Equal(LeaseStateEnum.Expired, reloaded.State);

        DhcpLeaseChangedEvent published = Assert.IsType<DhcpLeaseChangedEvent>(
            Assert.Single(_host.PublishedEvents)
        );
        Assert.Equal(DhcpLeaseChangeKindEnum.Expired, published.ChangeKind);
        Assert.Equal(lease.Id, published.LeaseId);
    }

    [Fact]
    public async Task SweepAsync_LeavesAnActiveLeaseNotYetOverdueAlone()
    {
        DhcpScopeEntity scope = await _host.SeedScopeAsync(
            "sweep-scope-2",
            "192.0.2.141",
            "192.0.2.141"
        );
        await SeedActiveLeaseAsync(
            scope.Name,
            "06:06:06:06:06:06",
            "192.0.2.141",
            DateTimeOffset.UtcNow.AddHours(1)
        );

        Dhcpv4LeaseExpirySweepService sweepService = GetSweepService();

        int expiredCount = await sweepService.SweepAsync();

        Assert.Equal(0, expiredCount);
        Assert.Empty(_host.PublishedEvents);

        LeaseEntity reloaded = await _host.FindLeaseAsync(scope.Name, "06:06:06:06:06:06");
        Assert.Equal(LeaseStateEnum.Active, reloaded.State);
    }

    private async Task<LeaseEntity> SeedActiveLeaseAsync(
        string scopeName,
        string clientIdentifier,
        string ipAddress,
        DateTimeOffset leaseExpiresAtUtc
    )
    {
        LeaseEntity lease = new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ProtocolVersion = InternetProtocolVersionEnum.IPv4,
            ScopeName = scopeName,
            ClientIdentifier = clientIdentifier,
            IpAddress = ipAddress,
            State = LeaseStateEnum.Active,
            LeaseObtainedAtUtc = DateTimeOffset.UtcNow.AddHours(-1),
            LeaseExpiresAtUtc = leaseExpiresAtUtc,
        };

        await using AsyncServiceScope scope = _host.Host.Services.CreateAsyncScope();
        ILeaseRepository repository = scope.ServiceProvider.GetRequiredService<ILeaseRepository>();
        await repository.AddAsync(lease);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

        return lease;
    }

    /// <summary>
    ///     This method resolves the real, singleton <see cref="Dhcpv4LeaseExpirySweepService" />
    ///     -- registered only as an <see cref="IHostedService" /> (via <c>AddHostedService</c>,
    ///     never under its own concrete type), so it must be found among every registered hosted
    ///     service rather than resolved directly -- the same pattern
    ///     <c>Ninlil.Dns.Server.Webhooks.Tests.WebhookDeliveryRetrySweepServiceTests.GetSweepService</c>
    ///     already establishes for its own comparable sweep service.
    /// </summary>
    private Dhcpv4LeaseExpirySweepService GetSweepService() =>
        _host
            .Host.Services.GetServices<IHostedService>()
            .OfType<Dhcpv4LeaseExpirySweepService>()
            .Single();
}
