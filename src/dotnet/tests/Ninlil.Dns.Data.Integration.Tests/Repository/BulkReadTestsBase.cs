using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Repository;

/// <summary>
///     This class holds the bulk-read tests that run against every engine, one subclass per
///     engine: <c>IRecordRepository.ListByZoneIdsAsync</c> (the zone-cache startup load) and
///     <c>IRoleRepository.ListClaimsForRolesAsync</c> (the claims of all of a user's roles). Each
///     returns the rows of the listed owners and nothing else.
/// </summary>
public abstract class BulkReadTestsBase(EngineFixtureBase fixture)
{
    /// <summary>
    ///     The records of the listed zones come back in one call, and a zone that is not listed
    ///     contributes none.
    /// </summary>
    [Fact]
    public async Task ListByZoneIdsAsync_ReturnsTheRecordsOfOnlyTheListedZones()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        Guid[] zoneIds = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];

        await using (AsyncServiceScope seed = database.Services.CreateAsyncScope())
        {
            IZoneRepository zones = seed.ServiceProvider.GetRequiredService<IZoneRepository>();
            IRecordRepository records =
                seed.ServiceProvider.GetRequiredService<IRecordRepository>();

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

                for (int record = 0; record <= zone; record++)
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

            await seed.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
        IRecordRepository repository =
            scope.ServiceProvider.GetRequiredService<IRecordRepository>();

        IReadOnlyList<RecordEntity> result = await repository.ListByZoneIdsAsync([
            zoneIds[1],
            zoneIds[2],
        ]);

        Assert.Equal(5, result.Count);
        Assert.Equal(2, result.Count(record => record.ZoneId == zoneIds[1]));
        Assert.Equal(3, result.Count(record => record.ZoneId == zoneIds[2]));
        Assert.Empty(await repository.ListByZoneIdsAsync([]));
    }

    /// <summary>
    ///     The claims of the listed roles come back in one call, each with its role id.
    /// </summary>
    [Fact]
    public async Task ListClaimsForRolesAsync_ReturnsTheClaimsOfOnlyTheListedRoles()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        Guid[] roleIds = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];

        await using (AsyncServiceScope seed = database.Services.CreateAsyncScope())
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

        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
        IRoleRepository repository = scope.ServiceProvider.GetRequiredService<IRoleRepository>();

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
            claim => Assert.True(claim.RoleId == roleIds[0] || claim.RoleId == roleIds[2])
        );
        Assert.Empty(await repository.ListClaimsForRolesAsync([]));
    }
}

/// <summary>This class runs <see cref="BulkReadTestsBase" /> on SQLite (no Docker).</summary>
[Collection(SqliteCollection.Name)]
public sealed class SqliteBulkReadTests(SqliteEngineFixture fixture) : BulkReadTestsBase(fixture);

/// <summary>This class runs <see cref="BulkReadTestsBase" /> on PostgreSQL (Docker).</summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class PostgreSqlBulkReadTests(PostgreSqlEngineFixture fixture)
    : BulkReadTestsBase(fixture);

/// <summary>This class runs <see cref="BulkReadTestsBase" /> on MySQL (Docker).</summary>
[Collection(MySqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MySqlBulkReadTests(MySqlEngineFixture fixture) : BulkReadTestsBase(fixture);

/// <summary>This class runs <see cref="BulkReadTestsBase" /> on SQL Server (Docker).</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class SqlServerBulkReadTests(SqlServerEngineFixture fixture)
    : BulkReadTestsBase(fixture);

/// <summary>This class runs <see cref="BulkReadTestsBase" /> on a MongoDB replica set (Docker).</summary>
[Collection(MongoDbCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoDbBulkReadTests(MongoDbEngineFixture fixture) : BulkReadTestsBase(fixture);

/// <summary>
///     This class runs <see cref="BulkReadTestsBase" /> on a standalone MongoDB (Docker).
/// </summary>
[Collection(MongoDbStandaloneCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoDbStandaloneBulkReadTests(MongoDbStandaloneEngineFixture fixture)
    : BulkReadTestsBase(fixture);
