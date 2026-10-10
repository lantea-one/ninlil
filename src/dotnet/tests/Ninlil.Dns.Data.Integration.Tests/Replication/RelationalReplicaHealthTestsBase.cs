using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Ninlil.Dns.Data.EntityFrameworkCore;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Replication;

/// <summary>
///     This class supplies the schema set-up of <see cref="ReplicaHealthTestsBase" /> for the
///     relational engines: the current database is created from the entity model, and the
///     "one step behind" database is migrated with the real EF migrations up to the one before the
///     newest, after which the newest is applied on the primary alone.
/// </summary>
/// <param name="host">The replicated host the subclass's fixture started.</param>
public abstract class RelationalReplicaHealthTestsBase(ReplicatedEngineHost host)
    : ReplicaHealthTestsBase(host)
{
    /// <summary>
    ///     This method makes the database exist on the primary and in the replication group when
    ///     the engine needs that before the schema is created (SQL Server). It does nothing by
    ///     default.
    /// </summary>
    protected virtual Task CreateReplicatedDatabaseAsync(string name) => Task.CompletedTask;

    /// <inheritdoc />
    protected override async Task<string> PrepareDatabaseAsync()
    {
        string name = $"health_{Guid.NewGuid():N}";
        await CreateReplicatedDatabaseAsync(name);
        await using EngineDatabase database = await Host.CreateDatabaseAsync(name);
        await Host.WaitForCatchUpAsync(name);
        return name;
    }

    /// <inheritdoc />
    protected override async Task<string> PrepareDatabaseOneStepBehindAsync()
    {
        string name = $"health_{Guid.NewGuid():N}";
        await CreateReplicatedDatabaseAsync(name);
        await using EngineDatabase database = await Host.CreateDatabaseAsync(
            name,
            createSchema: false
        );
        NinlilDnsContext context = database.CreateContext();
        List<string> migrations = [.. context.Database.GetMigrations()];
        Assert.True(migrations.Count >= 2);
        await context.GetService<IMigrator>().MigrateAsync(migrations[^2]);
        await Host.WaitForCatchUpAsync(name);
        return name;
    }

    /// <inheritdoc />
    protected override async Task ApplyLastSchemaStepOnPrimaryAsync(string databaseName)
    {
        await using EngineDatabase database = await Host.CreateDatabaseAsync(
            databaseName,
            createSchema: false
        );
        await database.CreateContext().Database.MigrateAsync();
    }
}
