using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dhcp.Data.EntityFrameworkCore.Tests.Repository;

/// <summary>
///     This class covers <see cref="Ninlil.Dhcp.Data.EntityFrameworkCore.Repository.LeaseRepository" />
///     against a temporary SQLite database file (<see cref="DhcpRepositorySearchTestHost" />):
///     that a <see cref="LeaseEntity" /> round-trips every field through the shared
///     <c>NinlilDnsContext</c>, and that the client, address and scope lookups find the right
///     rows.
/// </summary>
public sealed class LeaseRepositoryTests : IAsyncLifetime
{
    private readonly DhcpRepositorySearchTestHost _testHost = new();

    public Task InitializeAsync() => _testHost.InitializeAsync();

    public Task DisposeAsync() => _testHost.DisposeAsync().AsTask();

    [Fact]
    public async Task AddAsync_ThenGetByIdAsync_RoundTripsEveryField()
    {
        Guid leaseId = Guid.NewGuid();
        DateTimeOffset obtainedAtUtc = DateTimeOffset.UtcNow;
        DateTimeOffset expiresAtUtc = obtainedAtUtc.AddHours(24);

        await using (AsyncServiceScope writeScope = _testHost.Host.Services.CreateAsyncScope())
        {
            ILeaseRepository repository =
                writeScope.ServiceProvider.GetRequiredService<ILeaseRepository>();
            IUnitOfWork unitOfWork = writeScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await repository.AddAsync(
                new LeaseEntity
                {
                    Id = leaseId,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ProtocolVersion = InternetProtocolVersionEnum.IPv4,
                    ScopeName = "lease-round-trip-scope",
                    ClientIdentifier = "00:11:22:33:44:55",
                    IpAddress = "192.0.2.42",
                    HostName = "workstation-1",
                    State = LeaseStateEnum.Active,
                    LeaseObtainedAtUtc = obtainedAtUtc,
                    LeaseExpiresAtUtc = expiresAtUtc,
                }
            );

            await unitOfWork.SaveChangesAsync();
        }

        // Read back in a new scope so the row comes from SQLite, not the tracked instance just
        // written.
        await using AsyncServiceScope readScope = _testHost.Host.Services.CreateAsyncScope();
        ILeaseRepository readRepository =
            readScope.ServiceProvider.GetRequiredService<ILeaseRepository>();

        LeaseEntity lease = await readRepository.GetByIdAsync(leaseId);

        Assert.NotNull(lease);
        Assert.Equal(InternetProtocolVersionEnum.IPv4, lease.ProtocolVersion);
        Assert.Equal("lease-round-trip-scope", lease.ScopeName);
        Assert.Equal("00:11:22:33:44:55", lease.ClientIdentifier);
        Assert.Equal("192.0.2.42", lease.IpAddress);
        Assert.Equal("workstation-1", lease.HostName);
        Assert.Equal(LeaseStateEnum.Active, lease.State);
        Assert.Equal(obtainedAtUtc, lease.LeaseObtainedAtUtc);
        Assert.Equal(expiresAtUtc, lease.LeaseExpiresAtUtc);
    }

    [Fact]
    public async Task FindByClientIdentifierAsync_And_FindByIpAddressAsync_And_ListByScopeNameAsync_AllWork()
    {
        const string scopeName = "lease-lookup-scope";

        await using (AsyncServiceScope writeScope = _testHost.Host.Services.CreateAsyncScope())
        {
            ILeaseRepository repository =
                writeScope.ServiceProvider.GetRequiredService<ILeaseRepository>();
            IUnitOfWork unitOfWork = writeScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await repository.AddAsync(
                new LeaseEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ProtocolVersion = InternetProtocolVersionEnum.IPv4,
                    ScopeName = scopeName,
                    ClientIdentifier = "aa:aa:aa:aa:aa:aa",
                    IpAddress = "192.0.2.50",
                    State = LeaseStateEnum.Active,
                }
            );
            await repository.AddAsync(
                new LeaseEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ProtocolVersion = InternetProtocolVersionEnum.IPv4,
                    ScopeName = scopeName,
                    ClientIdentifier = "bb:bb:bb:bb:bb:bb",
                    IpAddress = "192.0.2.51",
                    State = LeaseStateEnum.Active,
                }
            );
            // A lease in a different scope, which must not appear in a lookup scoped to scopeName.
            await repository.AddAsync(
                new LeaseEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ProtocolVersion = InternetProtocolVersionEnum.IPv4,
                    ScopeName = "other-scope",
                    ClientIdentifier = "cc:cc:cc:cc:cc:cc",
                    IpAddress = "192.0.2.52",
                    State = LeaseStateEnum.Active,
                }
            );

            await unitOfWork.SaveChangesAsync();
        }

        await using AsyncServiceScope readScope = _testHost.Host.Services.CreateAsyncScope();
        ILeaseRepository readRepository =
            readScope.ServiceProvider.GetRequiredService<ILeaseRepository>();

        LeaseEntity byClient = await readRepository.FindByClientIdentifierAsync(
            scopeName,
            "aa:aa:aa:aa:aa:aa"
        );
        LeaseEntity byAddress = await readRepository.FindByIpAddressAsync(scopeName, "192.0.2.51");
        IReadOnlyList<LeaseEntity> scopeLeases = await readRepository.ListByScopeNameAsync(
            scopeName
        );

        Assert.NotNull(byClient);
        Assert.Equal("192.0.2.50", byClient.IpAddress);
        Assert.NotNull(byAddress);
        Assert.Equal("bb:bb:bb:bb:bb:bb", byAddress.ClientIdentifier);
        Assert.Equal(2, scopeLeases.Count);
        Assert.All(scopeLeases, lease => Assert.Equal(scopeName, lease.ScopeName));
    }

    /// <summary>
    ///     This test checks the query on SQLite: only an Active lease that has not expired (or
    ///     has no expiry) is returned, and released, expired-state and past-expiry rows are not.
    /// </summary>
    [Fact]
    public async Task ListActiveAsync_ReturnsOnlyActiveLeasesThatHaveNotExpired()
    {
        DateTimeOffset now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

        await using (AsyncServiceScope writeScope = _testHost.Host.Services.CreateAsyncScope())
        {
            ILeaseRepository repository =
                writeScope.ServiceProvider.GetRequiredService<ILeaseRepository>();
            IUnitOfWork unitOfWork = writeScope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await repository.AddAsync(
                NewLease("192.0.2.60", LeaseStateEnum.Active, now.AddHours(1))
            );
            await repository.AddAsync(NewLease("192.0.2.61", LeaseStateEnum.Active, null));
            await repository.AddAsync(
                NewLease("192.0.2.62", LeaseStateEnum.Active, now.AddHours(-1))
            );
            await repository.AddAsync(NewLease("192.0.2.63", LeaseStateEnum.Active, now));
            await repository.AddAsync(
                NewLease("192.0.2.64", LeaseStateEnum.Released, now.AddHours(1))
            );
            await repository.AddAsync(
                NewLease("192.0.2.65", LeaseStateEnum.Expired, now.AddHours(1))
            );
            await unitOfWork.SaveChangesAsync();
        }

        await using AsyncServiceScope readScope = _testHost.Host.Services.CreateAsyncScope();
        ILeaseRepository readRepository =
            readScope.ServiceProvider.GetRequiredService<ILeaseRepository>();

        IReadOnlyList<LeaseEntity> active = await readRepository.ListActiveAsync(now);

        Assert.Equal(
            ["192.0.2.60", "192.0.2.61"],
            active.Select(lease => lease.IpAddress).Order().ToArray()
        );
    }

    /// <summary>
    ///     This method builds a lease with a unique client identifier for the active-lease test.
    /// </summary>
    private static LeaseEntity NewLease(
        string ipAddress,
        LeaseStateEnum state,
        DateTimeOffset? expiresAtUtc
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ProtocolVersion = InternetProtocolVersionEnum.IPv4,
            ScopeName = "active-list-scope",
            ClientIdentifier = $"client-{ipAddress}",
            IpAddress = ipAddress,
            State = state,
            LeaseExpiresAtUtc = expiresAtUtc,
        };
}
