using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Xunit;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Repository;

/// <summary>
///     This class tests the read-path rules of the Entity Framework Core repositories against a
///     temporary SQLite database (<see cref="RepositorySearchTestHost" />): the bulk lookups
///     <c>IRecordRepository.ListByZoneIdsAsync</c> and <c>IRoleRepository.ListClaimsForRolesAsync</c>,
///     and that paging and snapshot reads return untracked entities while the reads that writers
///     use stay tracked.
/// </summary>
public sealed class ReadPathRepositoryTests : IAsyncLifetime
{
    private readonly RepositorySearchTestHost _testHost = new();

    public Task InitializeAsync() => _testHost.InitializeAsync();

    public Task DisposeAsync() => _testHost.DisposeAsync().AsTask();

    /// <summary>
    ///     This method seeds three zones with 2, 3 and 1 records.
    /// </summary>
    private async Task<Guid[]> SeedZonesAsync()
    {
        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IZoneRepository zones = scope.ServiceProvider.GetRequiredService<IZoneRepository>();
        IRecordRepository records = scope.ServiceProvider.GetRequiredService<IRecordRepository>();

        Guid[] zoneIds = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];
        int[] recordCounts = [2, 3, 1];

        for (int zone = 0; zone < zoneIds.Length; zone++)
        {
            await zones.AddAsync(
                new ZoneEntity
                {
                    Id = zoneIds[zone],
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    Name = $"zone-{zone}.example",
                    Type = ZoneTypeEnum.Primary,
                }
            );

            for (int record = 0; record < recordCounts[zone]; record++)
                await records.AddAsync(
                    new RecordEntity
                    {
                        Id = Guid.NewGuid(),
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                        ZoneId = zoneIds[zone],
                        Name = $"host-{record}.zone-{zone}.example",
                        Type = DnsResourceRecordTypeEnum.A,
                        TimeToLiveSeconds = 300,
                        DataJson = """{"Address":"192.0.2.1"}""",
                    }
                );
        }

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        return zoneIds;
    }

    [Fact]
    public async Task ListByZoneIdsAsync_ReturnsTheRecordsOfOnlyTheListedZones()
    {
        Guid[] zoneIds = await SeedZonesAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IRecordRepository records = scope.ServiceProvider.GetRequiredService<IRecordRepository>();

        IReadOnlyList<RecordEntity> result = await records.ListByZoneIdsAsync([
            zoneIds[0],
            zoneIds[1],
        ]);

        Assert.Equal(5, result.Count);
        Assert.Equal(2, result.Count(record => record.ZoneId == zoneIds[0]));
        Assert.Equal(3, result.Count(record => record.ZoneId == zoneIds[1]));
        Assert.DoesNotContain(result, record => record.ZoneId == zoneIds[2]);
    }

    [Fact]
    public async Task ListByZoneIdsAsync_WithNoZoneIds_ReturnsNothing()
    {
        await SeedZonesAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IRecordRepository records = scope.ServiceProvider.GetRequiredService<IRecordRepository>();

        Assert.Empty(await records.ListByZoneIdsAsync([]));
    }

    [Fact]
    public async Task ListByZoneIdsAsync_ReturnsUntrackedEntities()
    {
        Guid[] zoneIds = await SeedZonesAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IRecordRepository records = scope.ServiceProvider.GetRequiredService<IRecordRepository>();
        NinlilDnsContext context = scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();

        await records.ListByZoneIdsAsync(zoneIds);

        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task ListByZoneIdAsync_StaysTrackedBecauseWritersUseIt()
    {
        Guid[] zoneIds = await SeedZonesAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IRecordRepository records = scope.ServiceProvider.GetRequiredService<IRecordRepository>();
        NinlilDnsContext context = scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();

        IReadOnlyList<RecordEntity> result = await records.ListByZoneIdAsync(zoneIds[1]);

        Assert.Equal(result.Count, context.ChangeTracker.Entries<RecordEntity>().Count());
    }

    [Fact]
    public async Task SearchByZoneIdAsync_And_SearchAsync_ReturnUntrackedEntities()
    {
        Guid[] zoneIds = await SeedZonesAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IRecordRepository records = scope.ServiceProvider.GetRequiredService<IRecordRepository>();
        IZoneRepository zones = scope.ServiceProvider.GetRequiredService<IZoneRepository>();
        NinlilDnsContext context = scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();

        Assert.NotEmpty(await records.SearchByZoneIdAsync(zoneIds[1], 0, 10));
        Assert.NotEmpty(await zones.SearchAsync(0, 10));

        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task ListClaimsForRolesAsync_ReturnsTheClaimsOfOnlyTheListedRoles()
    {
        Guid[] roleIds = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];

        await using (AsyncServiceScope seed = _testHost.Host.Services.CreateAsyncScope())
        {
            IRoleRepository roles = seed.ServiceProvider.GetRequiredService<IRoleRepository>();

            for (int index = 0; index < roleIds.Length; index++)
            {
                await roles.AddAsync(
                    new RoleEntity
                    {
                        Id = roleIds[index],
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                        Name = $"role-{index}",
                        NormalizedName = $"ROLE-{index}",
                    }
                );
                await roles.AddClaimAsync(roleIds[index], "permission", $"a:{index}");
                await roles.AddClaimAsync(roleIds[index], "permission", $"b:{index}");
            }

            await seed.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        IRoleRepository repository = scope.ServiceProvider.GetRequiredService<IRoleRepository>();
        NinlilDnsContext context = scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();

        IReadOnlyList<RoleClaimEntity> claims = await repository.ListClaimsForRolesAsync([
            roleIds[0],
            roleIds[2],
        ]);

        Assert.Equal(
            ["a:0", "a:2", "b:0", "b:2"],
            claims.Select(claim => claim.ClaimValue).Order()
        );
        Assert.All(
            claims,
            claim => Assert.Contains(claim.RoleId, new[] { roleIds[0], roleIds[2] })
        );
        Assert.Empty(await repository.ListClaimsForRolesAsync([]));
        Assert.Empty(context.ChangeTracker.Entries<RoleClaimEntity>());
    }
}
