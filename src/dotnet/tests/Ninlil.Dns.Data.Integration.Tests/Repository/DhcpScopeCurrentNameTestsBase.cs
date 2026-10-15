using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Repository;

/// <summary>
///     This class holds the tests of <see cref="IDhcpScopeRepository.GetCurrentNameAsync" />,
///     which the DHCP allocation services use to notice a scope rename that committed while they
///     held a stale scope object. It runs against every engine, one subclass per engine setup
///     supplying the fixture.
/// </summary>
public abstract class DhcpScopeCurrentNameTestsBase(EngineFixtureBase fixture)
{
    /// <summary>
    ///     The committed name is returned even while the same context tracks a modified, unsaved
    ///     scope, because the read goes to the database and not to the tracked instance.
    /// </summary>
    [Fact]
    public async Task GetCurrentNameAsync_WhileTheSameContextTracksAnUnsavedRename_ReturnsTheCommittedName()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        Guid scopeId = Guid.NewGuid();

        await using (AsyncServiceScope seed = database.Services.CreateAsyncScope())
        {
            await seed
                .ServiceProvider.GetRequiredService<IDhcpScopeRepository>()
                .AddAsync(
                    new DhcpScopeEntity
                    {
                        Id = scopeId,
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                        Name = "committed",
                        ProtocolVersion = InternetProtocolVersionEnum.IPv4,
                        RangeStartAddress = "192.0.2.10",
                        RangeEndAddress = "192.0.2.200",
                        SubnetPrefixLength = 24,
                    }
                );
            await seed.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
        IDhcpScopeRepository repository =
            scope.ServiceProvider.GetRequiredService<IDhcpScopeRepository>();
        DhcpScopeEntity tracked = await repository.GetByIdAsync(scopeId);
        tracked.Name = "unsaved";
        repository.Update(tracked);

        Assert.Equal("committed", await repository.GetCurrentNameAsync(scopeId));
    }

    /// <summary>An unknown scope id gives null.</summary>
    [Fact]
    public async Task GetCurrentNameAsync_ForAnUnknownId_ReturnsNull()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();

        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
        IDhcpScopeRepository repository =
            scope.ServiceProvider.GetRequiredService<IDhcpScopeRepository>();

        Assert.Null(await repository.GetCurrentNameAsync(Guid.NewGuid()));
    }
}

/// <summary>This class runs <see cref="DhcpScopeCurrentNameTestsBase" /> on SQLite (no Docker).</summary>
[Collection(SqliteCollection.Name)]
public sealed class SqliteDhcpScopeCurrentNameTests(SqliteEngineFixture fixture)
    : DhcpScopeCurrentNameTestsBase(fixture);

/// <summary>This class runs <see cref="DhcpScopeCurrentNameTestsBase" /> on PostgreSQL (Docker).</summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class PostgreSqlDhcpScopeCurrentNameTests(PostgreSqlEngineFixture fixture)
    : DhcpScopeCurrentNameTestsBase(fixture);

/// <summary>This class runs <see cref="DhcpScopeCurrentNameTestsBase" /> on MySQL (Docker).</summary>
[Collection(MySqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MySqlDhcpScopeCurrentNameTests(MySqlEngineFixture fixture)
    : DhcpScopeCurrentNameTestsBase(fixture);

/// <summary>This class runs <see cref="DhcpScopeCurrentNameTestsBase" /> on SQL Server (Docker).</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class SqlServerDhcpScopeCurrentNameTests(SqlServerEngineFixture fixture)
    : DhcpScopeCurrentNameTestsBase(fixture);

/// <summary>This class runs <see cref="DhcpScopeCurrentNameTestsBase" /> on a MongoDB replica set (Docker).</summary>
[Collection(MongoDbCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoDbDhcpScopeCurrentNameTests(MongoDbEngineFixture fixture)
    : DhcpScopeCurrentNameTestsBase(fixture);

/// <summary>This class runs <see cref="DhcpScopeCurrentNameTestsBase" /> on a standalone MongoDB (Docker).</summary>
[Collection(MongoDbStandaloneCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoDbStandaloneDhcpScopeCurrentNameTests(
    MongoDbStandaloneEngineFixture fixture
) : DhcpScopeCurrentNameTestsBase(fixture);
