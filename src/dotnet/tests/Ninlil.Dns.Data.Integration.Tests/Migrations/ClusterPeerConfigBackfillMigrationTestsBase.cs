using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;
using Ninlil.Dns.Data.Integration.Tests.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Migrations;

/// <summary>
///     This class holds the tests of the <c>AddClusterPeerConfigBackfill</c> migration that run
///     against each relational engine; one subclass per engine supplies the fixture. One test
///     applies every migration to an empty database. The other applies the migrations up to the one
///     before it, stores a peer (the table the new one points at is unchanged by the migration,
///     so the current model can write it), applies the rest and checks that the peer survived,
///     that the new table takes a request for it, and that removing the peer cascades to the
///     request. The cascade and the unique index here come from the migration itself, not from
///     the model the other tests build their schema from.
/// </summary>
public abstract class ClusterPeerConfigBackfillMigrationTestsBase(EngineFixtureBase fixture)
{
    private const string MigrationName = "AddClusterPeerConfigBackfill";

    /// <summary>
    ///     On an empty database every migration applies and the new table is empty and usable.
    /// </summary>
    [Fact]
    public async Task Migrate_OnAnEmptyDatabase_CreatesAnEmptyUsableTable()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync(
            createSchema: false
        );
        await using NinlilDnsContext context = database.CreateContext();

        await context.Database.MigrateAsync();

        Assert.Empty(await context.ClusterPeerConfigBackfills.ToListAsync());
        ClusterNodeEntity node = ClusterPeerConfigBackfillTestsBase.NewNode("fresh");
        await ClusterPeerConfigBackfillTestsBase.SaveNodesAsync(database, node);
        await ClusterPeerConfigBackfillTestsBase.SaveRequestAsync(database, node.Id);
    }

    /// <summary>
    ///     A database at the previous migration with a peer in it keeps the peer, accepts one
    ///     request per peer, and drops the request with the peer.
    /// </summary>
    [Fact]
    public async Task Migrate_WithAPeerPresent_KeepsThePeerAndCascadesTheRequest()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync(
            createSchema: false
        );
        await using NinlilDnsContext context = database.CreateContext();

        List<string> migrations = [.. context.Database.GetMigrations()];
        int index = migrations.FindIndex(name => name.EndsWith("_" + MigrationName));
        Assert.True(index > 0, "The migration is missing from the provider's assembly.");
        await context.GetService<IMigrator>().MigrateAsync(migrations[index - 1]);

        ClusterNodeEntity node = ClusterPeerConfigBackfillTestsBase.NewNode("existing");
        context.ClusterNodes.Add(node);
        await context.SaveChangesAsync();

        await context.Database.MigrateAsync();

        await ClusterPeerConfigBackfillTestsBase.SaveRequestAsync(database, node.Id);
        await using (AsyncServiceScope scope = database.Services.CreateAsyncScope())
        {
            IClusterNodeRepository nodes =
                scope.ServiceProvider.GetRequiredService<IClusterNodeRepository>();
            ClusterNodeEntity found = await nodes.GetByIdAsync(node.Id);
            Assert.Equal("existing", found?.Name);
            nodes.Remove(found);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await using AsyncServiceScope check = database.Services.CreateAsyncScope();
        Assert.Empty(
            await check
                .ServiceProvider.GetRequiredService<IClusterPeerConfigBackfillRepository>()
                .ListPendingClusterNodeIdsAsync()
        );
    }
}

/// <summary>This class runs <see cref="ClusterPeerConfigBackfillMigrationTestsBase" /> on SQLite (a temporary file, no Docker).</summary>
[Collection(SqliteCollection.Name)]
public sealed class SqliteClusterPeerConfigBackfillMigrationTests(SqliteEngineFixture fixture)
    : ClusterPeerConfigBackfillMigrationTestsBase(fixture);

/// <summary>This class runs <see cref="ClusterPeerConfigBackfillMigrationTestsBase" /> on PostgreSQL (Docker).</summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class PostgreSqlClusterPeerConfigBackfillMigrationTests(
    PostgreSqlEngineFixture fixture
) : ClusterPeerConfigBackfillMigrationTestsBase(fixture);

/// <summary>This class runs <see cref="ClusterPeerConfigBackfillMigrationTestsBase" /> on MySQL (Docker).</summary>
[Collection(MySqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MySqlClusterPeerConfigBackfillMigrationTests(MySqlEngineFixture fixture)
    : ClusterPeerConfigBackfillMigrationTestsBase(fixture);

/// <summary>This class runs <see cref="ClusterPeerConfigBackfillMigrationTestsBase" /> on SQL Server (Docker).</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class SqlServerClusterPeerConfigBackfillMigrationTests(SqlServerEngineFixture fixture)
    : ClusterPeerConfigBackfillMigrationTestsBase(fixture);
