using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Repository;

/// <summary>
///     This class holds the tests of <see cref="DnsFilterListEntity.AppliesToAllClients" /> that
///     run against every engine: the flag is stored and read back, and a list written without it
///     reads as false. One subclass per engine supplies the fixture and a fresh database.
/// </summary>
public abstract class FilterListAppliesToAllClientsTestsBase(EngineFixtureBase fixture)
{
    private static DnsFilterListEntity List(string name, bool appliesToAllClients) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Kind = DnsFilterListKindEnum.Domain,
            Action = DnsFilterListActionEnum.Block,
            Source = DnsFilterListSourceEnum.Local,
            AppliesToAllClients = appliesToAllClients,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

    /// <summary>
    ///     A list saved with the flag set reads back with it set, and one saved without it reads
    ///     back false.
    /// </summary>
    [Fact]
    public async Task ListAsync_ReturnsTheStoredAppliesToAllClientsFlag()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        DnsFilterListEntity global = List("global", true);
        DnsFilterListEntity assigned = List("assigned", false);

        await using (AsyncServiceScope writeScope = database.Services.CreateAsyncScope())
        {
            IDnsFilterListRepository writer =
                writeScope.ServiceProvider.GetRequiredService<IDnsFilterListRepository>();
            await writer.AddAsync(global);
            await writer.AddAsync(assigned);
            await writeScope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await using AsyncServiceScope readScope = database.Services.CreateAsyncScope();
        IReadOnlyList<DnsFilterListEntity> lists = await readScope
            .ServiceProvider.GetRequiredService<IDnsFilterListRepository>()
            .ListAsync(list => list.AppliesToAllClients);

        Assert.Equal(global.Id, Assert.Single(lists).Id);
        Assert.False(
            (
                await readScope
                    .ServiceProvider.GetRequiredService<IDnsFilterListRepository>()
                    .GetByIdAsync(assigned.Id)
            ).AppliesToAllClients
        );
    }
}

/// <summary>This class runs <see cref="FilterListAppliesToAllClientsTestsBase" /> on SQLite (no Docker).</summary>
[Collection(SqliteCollection.Name)]
public sealed class SqliteFilterListAppliesToAllClientsTests(SqliteEngineFixture fixture)
    : FilterListAppliesToAllClientsTestsBase(fixture);

/// <summary>This class runs <see cref="FilterListAppliesToAllClientsTestsBase" /> on PostgreSQL (Docker).</summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class PostgreSqlFilterListAppliesToAllClientsTests(PostgreSqlEngineFixture fixture)
    : FilterListAppliesToAllClientsTestsBase(fixture);

/// <summary>This class runs <see cref="FilterListAppliesToAllClientsTestsBase" /> on MySQL (Docker).</summary>
[Collection(MySqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MySqlFilterListAppliesToAllClientsTests(MySqlEngineFixture fixture)
    : FilterListAppliesToAllClientsTestsBase(fixture);

/// <summary>This class runs <see cref="FilterListAppliesToAllClientsTestsBase" /> on SQL Server (Docker).</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class SqlServerFilterListAppliesToAllClientsTests(SqlServerEngineFixture fixture)
    : FilterListAppliesToAllClientsTestsBase(fixture);

/// <summary>
///     This class runs <see cref="FilterListAppliesToAllClientsTestsBase" /> on a MongoDB replica set (Docker).
/// </summary>
[Collection(MongoDbCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoDbFilterListAppliesToAllClientsTests(MongoDbEngineFixture fixture)
    : FilterListAppliesToAllClientsTestsBase(fixture);

/// <summary>
///     This class runs <see cref="FilterListAppliesToAllClientsTestsBase" /> on a standalone MongoDB (Docker).
/// </summary>
[Collection(MongoDbStandaloneCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoDbStandaloneFilterListAppliesToAllClientsTests(
    MongoDbStandaloneEngineFixture fixture
) : FilterListAppliesToAllClientsTestsBase(fixture);
