using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Enum;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Repository;

/// <summary>
///     This class holds the test of the step <c>DhcpScopesController</c> takes when a scope is
///     renamed: load the leases of the old name, set their <c>ScopeName</c>, stage them with
///     <c>ILeaseRepository.Update</c> and save once. It runs against every engine, one
///     subclass per engine setup supplying the fixture, because a tracked relational update and a
///     MongoDB replace stage the change differently.
/// </summary>
public abstract class LeaseRenameTestsBase(EngineFixtureBase fixture)
{
    /// <summary>
    ///     Leases moved to a new scope name are found under it, are gone from the old name, and a
    ///     lease of another scope is untouched.
    /// </summary>
    [Fact]
    public async Task RenamedLeases_AreFoundUnderTheNewNameOnly()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();

        await using (AsyncServiceScope seed = database.Services.CreateAsyncScope())
        {
            ILeaseRepository leases = seed.ServiceProvider.GetRequiredService<ILeaseRepository>();
            await leases.AddAsync(NewLease("old", "10.0.0.1"));
            await leases.AddAsync(NewLease("old", "10.0.0.2"));
            await leases.AddAsync(NewLease("other", "10.0.0.1"));
            await seed.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await using (AsyncServiceScope rename = database.Services.CreateAsyncScope())
        {
            ILeaseRepository leases = rename.ServiceProvider.GetRequiredService<ILeaseRepository>();
            foreach (LeaseEntity lease in await leases.ListByScopeNameAsync("old"))
            {
                lease.ScopeName = "new";
                leases.Update(lease);
            }

            await rename.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await using AsyncServiceScope read = database.Services.CreateAsyncScope();
        ILeaseRepository readLeases = read.ServiceProvider.GetRequiredService<ILeaseRepository>();
        Assert.Equal(2, (await readLeases.ListByScopeNameAsync("new")).Count);
        Assert.Empty(await readLeases.ListByScopeNameAsync("old"));
        Assert.Single(await readLeases.ListByScopeNameAsync("other"));
    }

    private static LeaseEntity NewLease(string scopeName, string address) =>
        new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ProtocolVersion = InternetProtocolVersionEnum.IPv4,
            ScopeName = scopeName,
            ClientIdentifier = $"client-{scopeName}-{address}",
            IpAddress = address,
            State = LeaseStateEnum.Active,
            LeaseObtainedAtUtc = DateTimeOffset.UtcNow,
            LeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddDays(1),
        };
}

/// <summary>This class runs <see cref="LeaseRenameTestsBase" /> on SQLite (no Docker).</summary>
[Collection(SqliteCollection.Name)]
public sealed class SqliteLeaseRenameTests(SqliteEngineFixture fixture)
    : LeaseRenameTestsBase(fixture);

/// <summary>This class runs <see cref="LeaseRenameTestsBase" /> on PostgreSQL (Docker).</summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class PostgreSqlLeaseRenameTests(PostgreSqlEngineFixture fixture)
    : LeaseRenameTestsBase(fixture);

/// <summary>This class runs <see cref="LeaseRenameTestsBase" /> on MySQL (Docker).</summary>
[Collection(MySqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MySqlLeaseRenameTests(MySqlEngineFixture fixture)
    : LeaseRenameTestsBase(fixture);

/// <summary>This class runs <see cref="LeaseRenameTestsBase" /> on SQL Server (Docker).</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class SqlServerLeaseRenameTests(SqlServerEngineFixture fixture)
    : LeaseRenameTestsBase(fixture);

/// <summary>This class runs <see cref="LeaseRenameTestsBase" /> on a MongoDB replica set (Docker).</summary>
[Collection(MongoDbCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoDbLeaseRenameTests(MongoDbEngineFixture fixture)
    : LeaseRenameTestsBase(fixture);

/// <summary>This class runs <see cref="LeaseRenameTestsBase" /> on a standalone MongoDB (Docker).</summary>
[Collection(MongoDbStandaloneCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoDbStandaloneLeaseRenameTests(MongoDbStandaloneEngineFixture fixture)
    : LeaseRenameTestsBase(fixture);
