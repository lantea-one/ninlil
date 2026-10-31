using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Exceptions;
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Maintenance;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests;

/// <summary>
///     This class holds the tests of <see cref="RelationalStartupMigrator" /> that run against
///     each relational engine; one subclass per engine supplies the fixture. Each test creates an
///     empty database without a schema and brings it to a chosen migration with the
///     <c>IMigrator</c>. The failure test makes the <c>AddZoneValidateDnssec</c> migration fail by
///     adding the column it adds before it runs, with two migrations pending ahead of the end, so
///     it shows whether the earlier one stays applied. A migration with a hand-made column clash
///     is the failure here because no engine can be made to refuse a valid migration on demand.
/// </summary>
public abstract class RelationalStartupMigratorTestsBase(EngineFixtureBase fixture)
{
    private const string FailingMigration = "AddZoneValidateDnssec";

    private StorageOptions Options(string connectionString) =>
        new() { Engine = fixture.Engine, ConnectionString = connectionString };

    private string Quote(string name) =>
        fixture.Engine switch
        {
            StorageEngineEnum.MySql => $"`{name}`",
            StorageEngineEnum.SqlServer => $"[{name}]",
            _ => $"\"{name}\"",
        };

    private Task MigrateToAsync(NinlilDnsContext context, string target) =>
        context.GetService<IMigrator>().MigrateAsync(target);

    private static Task RunAsync(NinlilDnsContext context, StorageOptions options) =>
        RelationalStartupMigrator.RunAsync(context, options, NullLogger.Instance);

    /// <summary>
    ///     A database with no tables at all is migrated to the newest migration without a refusal.
    /// </summary>
    [Fact]
    public async Task RunAsync_OnAFreshDatabase_AppliesEveryMigration()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync(
            createSchema: false
        );
        NinlilDnsContext context = database.CreateContext();

        await RunAsync(context, Options(database.ConnectionString));

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.Equal(
            context.Database.GetMigrations().ToList(),
            (await context.Database.GetAppliedMigrationsAsync()).ToList()
        );
    }

    /// <summary>
    ///     On a server with no database yet, the migrator and the EF Core context it drives log
    ///     nothing at Error or above, and the fresh start is announced at Information. The
    ///     failed open of the missing database is expected, not an error.
    /// </summary>
    [Fact]
    public async Task RunAsync_OnAServerWithoutTheDatabase_LogsNoError()
    {
        CapturingLoggerProvider captured = new();
        await using EngineDatabase database = await fixture.CreateDatabaseAsync(
            createSchema: false,
            configureServices: (services, _) => services.AddSingleton<ILoggerProvider>(captured)
        );
        NinlilDnsContext context = database.CreateContext();
        ILogger logger = database
            .Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Test");

        await RelationalStartupMigrator.RunAsync(
            context,
            Options(database.ConnectionString),
            logger
        );

        Assert.DoesNotContain(captured.Entries, entry => entry.Level >= LogLevel.Error);
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        if (fixture.Engine != StorageEngineEnum.Sqlite)
            Assert.Contains(
                captured.Entries,
                entry =>
                    entry.Level == LogLevel.Information
                    && entry.Message.Contains("does not exist yet")
            );
    }

    /// <summary>
    ///     A database one migration behind is migrated to the newest migration without a refusal.
    /// </summary>
    [Fact]
    public async Task RunAsync_WithPendingMigrations_AppliesThem()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync(
            createSchema: false
        );
        NinlilDnsContext context = database.CreateContext();
        List<string> migrations = [.. context.Database.GetMigrations()];
        await MigrateToAsync(context, migrations[^2]);

        await RunAsync(context, Options(database.ConnectionString));

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
    }

    /// <summary>
    ///     A history row for a migration this build does not have makes the migrator refuse,
    ///     name the row, and leave the pending migrations unapplied.
    /// </summary>
    [Fact]
    public async Task RunAsync_WithAMigrationFromANewerVersion_RefusesAndMigratesNothing()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync(
            createSchema: false
        );
        NinlilDnsContext context = database.CreateContext();
        List<string> migrations = [.. context.Database.GetMigrations()];
        await MigrateToAsync(context, migrations[^2]);
        await context.Database.ExecuteSqlRawAsync(
            $"INSERT INTO {Quote("__EFMigrationsHistory")} ({Quote("MigrationId")}, "
                + $"{Quote("ProductVersion")}) VALUES ('20991231000000_FromTheFuture', '99.0.0')"
        );

        DatabaseStartupRefusedException refusal =
            await Assert.ThrowsAsync<DatabaseStartupRefusedException>(() =>
                RunAsync(context, Options(database.ConnectionString))
            );

        Assert.Contains("20991231000000_FromTheFuture", refusal.Message);
        Assert.Contains("newer Ninlil version", refusal.Message);
        Assert.Equal(
            [migrations[^1]],
            (await context.Database.GetPendingMigrationsAsync()).ToList()
        );
    }

    /// <summary>
    ///     This method pre-creates the column the <c>AddZoneValidateDnssec</c> migration adds,
    ///     starting from the migration two before it, and runs the migrator, which must refuse.
    ///     The caller inspects the refusal and the history.
    /// </summary>
    protected async Task<(
        EngineDatabase Database,
        DatabaseStartupRefusedException Refusal,
        List<string> AppliedBefore,
        NinlilDnsContext Context
    )> RunWithAFailingMigrationAsync()
    {
        EngineDatabase database = await fixture.CreateDatabaseAsync(createSchema: false);
        NinlilDnsContext context = database.CreateContext();
        List<string> migrations = [.. context.Database.GetMigrations()];
        int failing = migrations.FindIndex(name => name.EndsWith("_" + FailingMigration));
        Assert.True(failing >= 2, "The failing migration is missing from the provider's assembly.");
        await MigrateToAsync(context, migrations[failing - 2]);

        string booleanType = fixture.Engine switch
        {
            StorageEngineEnum.PostgreSql => "boolean",
            StorageEngineEnum.SqlServer => "bit",
            StorageEngineEnum.MySql => "tinyint(1)",
            _ => "INTEGER",
        };
        string addColumn =
            $"ALTER TABLE {Quote("NinlilDnsZones")} ADD {Quote("ValidateDnssec")} {booleanType} NULL";
        await context.Database.ExecuteSqlRawAsync(addColumn);

        List<string> appliedBefore = [.. await context.Database.GetAppliedMigrationsAsync()];
        DatabaseStartupRefusedException refusal =
            await Assert.ThrowsAsync<DatabaseStartupRefusedException>(() =>
                RunAsync(context, Options(database.ConnectionString))
            );
        return (database, refusal, appliedBefore, context);
    }

    /// <summary>
    ///     On the engines with transactional DDL a failed batch leaves the history as it was, and
    ///     the refusal says so. The MySQL subclass overrides this to expect the opposite.
    /// </summary>
    [Fact]
    public virtual async Task RunAsync_WhenAMigrationFails_ReportsWhetherItWasRolledBack()
    {
        (
            EngineDatabase database,
            DatabaseStartupRefusedException refusal,
            List<string> appliedBefore,
            NinlilDnsContext context
        ) = await RunWithAFailingMigrationAsync();
        await using EngineDatabase _ = database;

        Assert.Contains("was rolled back; the database is unchanged", refusal.Message);
        Assert.NotNull(refusal.InnerException);
        Assert.Equal(appliedBefore, (await context.Database.GetAppliedMigrationsAsync()).ToList());
    }
}
