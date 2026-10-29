using System.Collections.Concurrent;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dhcp.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Server.Core.ClientIdentity;
using Ninlil.Dns.Server.Core.Events;
using Xunit;

// Define our namespace.
namespace Ninlil.Dhcp.Server.DnsIntegration.Tests;

/// <summary>
///     This class covers <see cref="DhcpLeaseClientIdentityResolver" /> on an
///     <see cref="ILeaseRepository" /> over a temporary SQLite file: a source address resolves to
///     the identifier of the active lease holding it, in any scope, and inactive leases and
///     unknown addresses resolve to nothing. The resolver answers from an in-memory snapshot, so
///     the tests also count repository calls, move the clock by hand and publish
///     <see cref="DhcpLeaseChangedEvent" /> to the invalidator.
/// </summary>
public sealed class DhcpLeaseClientIdentityResolverTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dhcp-lease-client-identity-tests-{Guid.NewGuid()}.sqlite3"
    );

    private readonly ConcurrentDictionary<string, int> _repositoryCalls = new();

    private readonly ManualTimeProvider _time = new();

    private IHost _host;

    public async Task InitializeAsync()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder
            .Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration)
            .AddNinlilDhcpEntityFrameworkCore();
        CallCountingProxy<ILeaseRepository>.Decorate(builder.Services, _repositoryCalls);
        builder.Services.AddSingleton<TimeProvider>(_time);
        builder.Services.AddSingleton<DhcpLeaseClientIdentityResolver>();
        builder.Services.AddSingleton<
            IDnsServerEventSubscriber<DhcpLeaseChangedEvent>,
            DhcpLeaseIdentitySnapshotInvalidator
        >();

        _host = builder.Build();

        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        NinlilDnsContext context = scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync()
    {
        _host.Dispose();

        TemporaryFiles.DeleteSqliteDatabase(_databasePath);

        return Task.CompletedTask;
    }

    [Fact]
    public async Task ResolveAsync_WithAnAddressHeldByAnActiveDhcpv4Lease_ReturnsItsMacAddress()
    {
        await using (AsyncServiceScope seedScope = _host.Services.CreateAsyncScope())
        {
            ILeaseRepository leaseRepository =
                seedScope.ServiceProvider.GetRequiredService<ILeaseRepository>();
            IUnitOfWork unitOfWork = seedScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await leaseRepository.AddAsync(
                new LeaseEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ProtocolVersion = InternetProtocolVersionEnum.IPv4,
                    ScopeName = "resolver-test-scope",
                    ClientIdentifier = "aa:bb:cc:dd:ee:ff",
                    IpAddress = "192.0.2.77",
                    State = LeaseStateEnum.Active,
                }
            );
            await unitOfWork.SaveChangesAsync();
        }

        DhcpLeaseClientIdentityResolver resolver =
            _host.Services.GetRequiredService<DhcpLeaseClientIdentityResolver>();

        ClientIdentityResolution resolution = await resolver.ResolveAsync(
            IPAddress.Parse("192.0.2.77")
        );

        Assert.NotNull(resolution);
        Assert.Equal("aa:bb:cc:dd:ee:ff", resolution.ClientIdentifier);
        Assert.Equal(InternetProtocolVersionEnum.IPv4, resolution.ProtocolVersion);
    }

    [Fact]
    public async Task ResolveAsync_WithAnAddressHeldByAnActiveDhcpv6Lease_ReturnsItsDuid()
    {
        await using (AsyncServiceScope seedScope = _host.Services.CreateAsyncScope())
        {
            ILeaseRepository leaseRepository =
                seedScope.ServiceProvider.GetRequiredService<ILeaseRepository>();
            IUnitOfWork unitOfWork = seedScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await leaseRepository.AddAsync(
                new LeaseEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ProtocolVersion = InternetProtocolVersionEnum.IPv6,
                    ScopeName = "resolver-test-scope-v6",
                    ClientIdentifier = "00:03:00:01:aa:bb:cc:dd:ee:ff",
                    IpAddress = "2001:db8::42",
                    State = LeaseStateEnum.Active,
                }
            );
            await unitOfWork.SaveChangesAsync();
        }

        DhcpLeaseClientIdentityResolver resolver =
            _host.Services.GetRequiredService<DhcpLeaseClientIdentityResolver>();

        ClientIdentityResolution resolution = await resolver.ResolveAsync(
            IPAddress.Parse("2001:db8::42")
        );

        Assert.NotNull(resolution);
        Assert.Equal("00:03:00:01:aa:bb:cc:dd:ee:ff", resolution.ClientIdentifier);
        Assert.Equal(InternetProtocolVersionEnum.IPv6, resolution.ProtocolVersion);
    }

    [Fact]
    public async Task ResolveAsync_WithAnAddressHeldOnlyByAReleasedLease_ReturnsNull()
    {
        await using (AsyncServiceScope seedScope = _host.Services.CreateAsyncScope())
        {
            ILeaseRepository leaseRepository =
                seedScope.ServiceProvider.GetRequiredService<ILeaseRepository>();
            IUnitOfWork unitOfWork = seedScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await leaseRepository.AddAsync(
                new LeaseEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ProtocolVersion = InternetProtocolVersionEnum.IPv4,
                    ScopeName = "resolver-test-scope",
                    ClientIdentifier = "11:22:33:44:55:66",
                    IpAddress = "192.0.2.88",
                    State = LeaseStateEnum.Released,
                }
            );
            await unitOfWork.SaveChangesAsync();
        }

        DhcpLeaseClientIdentityResolver resolver =
            _host.Services.GetRequiredService<DhcpLeaseClientIdentityResolver>();

        ClientIdentityResolution resolution = await resolver.ResolveAsync(
            IPAddress.Parse("192.0.2.88")
        );

        Assert.Null(resolution);
    }

    [Fact]
    public async Task ResolveAsync_WithAnAddressNoLeaseEverHeld_ReturnsNull()
    {
        DhcpLeaseClientIdentityResolver resolver =
            _host.Services.GetRequiredService<DhcpLeaseClientIdentityResolver>();

        ClientIdentityResolution resolution = await resolver.ResolveAsync(
            IPAddress.Parse("192.0.2.99")
        );

        Assert.Null(resolution);
    }

    /// <summary>
    ///     This test checks that an IPv4-mapped IPv6 source address ("::ffff:x.x.x.x"), which is
    ///     how IPv4 clients arrive from the dual-stack DNS listeners, matches a lease stored in
    ///     dotted-quad form in <see cref="LeaseEntity.IpAddress" />. Without unmapping in
    ///     <see cref="DhcpLeaseClientIdentityResolver.ResolveAsync" />, no IPv4 query would ever
    ///     resolve.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WithAnIPv4MappedIPv6Address_ResolvesTheSameAsItsPlainIPv4Form()
    {
        await using (AsyncServiceScope seedScope = _host.Services.CreateAsyncScope())
        {
            ILeaseRepository leaseRepository =
                seedScope.ServiceProvider.GetRequiredService<ILeaseRepository>();
            IUnitOfWork unitOfWork = seedScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await leaseRepository.AddAsync(
                new LeaseEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ProtocolVersion = InternetProtocolVersionEnum.IPv4,
                    ScopeName = "resolver-test-scope",
                    ClientIdentifier = "77:88:99:aa:bb:cc",
                    IpAddress = "192.0.2.123",
                    State = LeaseStateEnum.Active,
                }
            );
            await unitOfWork.SaveChangesAsync();
        }

        DhcpLeaseClientIdentityResolver resolver =
            _host.Services.GetRequiredService<DhcpLeaseClientIdentityResolver>();

        ClientIdentityResolution resolution = await resolver.ResolveAsync(
            IPAddress.Parse("192.0.2.123").MapToIPv6()
        );

        Assert.NotNull(resolution);
        Assert.Equal("77:88:99:aa:bb:cc", resolution.ClientIdentifier);
        Assert.Equal(InternetProtocolVersionEnum.IPv4, resolution.ProtocolVersion);
    }

    /// <summary>
    ///     This method seeds one lease through the repository, in a new scope.
    /// </summary>
    private async Task SeedLeaseAsync(
        string ipAddress,
        string clientIdentifier,
        LeaseStateEnum state,
        DateTimeOffset? expiresAtUtc
    )
    {
        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<ILeaseRepository>()
            .AddAsync(
                new LeaseEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ProtocolVersion = InternetProtocolVersionEnum.IPv4,
                    ScopeName = "snapshot-test-scope",
                    ClientIdentifier = clientIdentifier,
                    IpAddress = ipAddress,
                    State = state,
                    LeaseExpiresAtUtc = expiresAtUtc,
                }
            );
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    /// <summary>
    ///     This test checks that queries are answered from the in-memory snapshot: 100 resolves
    ///     read the repository once.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_ManyCalls_ReadTheRepositoryOnlyOnce()
    {
        await SeedLeaseAsync(
            "192.0.2.10",
            "aa:aa:aa:aa:aa:01",
            LeaseStateEnum.Active,
            _time.GetUtcNow().AddHours(1)
        );
        DhcpLeaseClientIdentityResolver resolver =
            _host.Services.GetRequiredService<DhcpLeaseClientIdentityResolver>();

        for (int i = 0; i < 100; i++)
            Assert.NotNull(await resolver.ResolveAsync(IPAddress.Parse("192.0.2.10")));

        Assert.Equal(1, _repositoryCalls["ListActiveAsync"]);
        Assert.False(_repositoryCalls.ContainsKey("FindActiveByIpAddressAsync"));
    }

    /// <summary>
    ///     This test checks that a lease whose expiry time has passed is ignored: it resolves
    ///     while valid and stops resolving once the clock passes its expiry, even though the
    ///     snapshot still holds it.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_WithALeaseThatHasExpired_ReturnsNull()
    {
        await SeedLeaseAsync(
            "192.0.2.11",
            "aa:aa:aa:aa:aa:02",
            LeaseStateEnum.Active,
            _time.GetUtcNow().AddMinutes(10)
        );
        DhcpLeaseClientIdentityResolver resolver =
            _host.Services.GetRequiredService<DhcpLeaseClientIdentityResolver>();
        Assert.NotNull(await resolver.ResolveAsync(IPAddress.Parse("192.0.2.11")));

        _time.Advance(TimeSpan.FromMinutes(10));

        Assert.Null(await resolver.ResolveAsync(IPAddress.Parse("192.0.2.11")));
    }

    [Fact]
    public async Task ResolveAsync_WithAnActiveLeaseAlreadyPastItsExpiry_ReturnsNull()
    {
        await SeedLeaseAsync(
            "192.0.2.12",
            "aa:aa:aa:aa:aa:03",
            LeaseStateEnum.Active,
            _time.GetUtcNow().AddMinutes(-1)
        );
        DhcpLeaseClientIdentityResolver resolver =
            _host.Services.GetRequiredService<DhcpLeaseClientIdentityResolver>();

        Assert.Null(await resolver.ResolveAsync(IPAddress.Parse("192.0.2.12")));
    }

    /// <summary>
    ///     This test checks that a <see cref="DhcpLeaseChangedEvent" /> handled by the registered
    ///     invalidator makes a lease granted after the snapshot was built resolve, without the
    ///     time-to-live passing.
    /// </summary>
    [Fact]
    public async Task LeaseChangedEvent_RefreshesTheSnapshotSoANewLeaseResolves()
    {
        DhcpLeaseClientIdentityResolver resolver =
            _host.Services.GetRequiredService<DhcpLeaseClientIdentityResolver>();
        Assert.Null(await resolver.ResolveAsync(IPAddress.Parse("192.0.2.13")));
        await SeedLeaseAsync(
            "192.0.2.13",
            "aa:aa:aa:aa:aa:04",
            LeaseStateEnum.Active,
            _time.GetUtcNow().AddHours(1)
        );
        // Not stale yet, so the new lease is not visible without the event.
        Assert.Null(await resolver.ResolveAsync(IPAddress.Parse("192.0.2.13")));

        IDnsServerEventSubscriber<DhcpLeaseChangedEvent> invalidator = _host
            .Services.GetServices<IDnsServerEventSubscriber<DhcpLeaseChangedEvent>>()
            .OfType<DhcpLeaseIdentitySnapshotInvalidator>()
            .Single();
        await invalidator.HandleAsync(
            new DhcpLeaseChangedEvent(
                _time.GetUtcNow(),
                Guid.NewGuid(),
                DhcpLeaseChangeKindEnum.Granted
            ),
            CancellationToken.None
        );
        await resolver.WhenIdleAsync();

        ClientIdentityResolution resolution = await resolver.ResolveAsync(
            IPAddress.Parse("192.0.2.13")
        );
        Assert.Equal("aa:aa:aa:aa:aa:04", resolution?.ClientIdentifier);
    }

    [Fact]
    public async Task ResolveAsync_WhenARefreshFails_KeepsAnsweringFromTheLastGoodSnapshot()
    {
        await SeedLeaseAsync(
            "192.0.2.14",
            "aa:aa:aa:aa:aa:05",
            LeaseStateEnum.Active,
            _time.GetUtcNow().AddHours(1)
        );
        DhcpLeaseClientIdentityResolver resolver =
            _host.Services.GetRequiredService<DhcpLeaseClientIdentityResolver>();
        Assert.NotNull(await resolver.ResolveAsync(IPAddress.Parse("192.0.2.14")));

        // Dropping the table makes the next refresh fail inside the repository.
        await using (AsyncServiceScope scope = _host.Services.CreateAsyncScope())
        {
            NinlilDnsContext context = scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
            await context.Database.ExecuteSqlRawAsync("DROP TABLE NinlilDhcpLeases");
        }

        _time.Advance(DhcpLeaseClientIdentityResolver.SnapshotTimeToLive);
        Assert.NotNull(await resolver.ResolveAsync(IPAddress.Parse("192.0.2.14")));
        await resolver.WhenIdleAsync();

        Assert.NotNull(await resolver.ResolveAsync(IPAddress.Parse("192.0.2.14")));
        Assert.Equal(2, _repositoryCalls["ListActiveAsync"]);
    }
}
