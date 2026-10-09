using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Data.Abstractions.Exceptions;
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Maintenance;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests;

/// <summary>
///     This class tests the migration snapshots that <see cref="RelationalStartupMigrator" />
///     takes on SQLite: when one is taken, the failed-migration marker, retention and the restore
///     setting. It uses real SQLite files in the temporary directory of
///     <see cref="SqliteEngineFixture" />, so it needs no Docker and carries no
///     <c>RequiresDocker</c> trait. The SQLite store is tested here rather than in a Docker-free
///     unit-test project because the migrations of the real EF model are the thing under test.
///     A failed migration cannot be provoked on SQLite (it rolls the batch back), so the marker
///     is written through the store.
/// </summary>
[Collection(SqliteCollection.Name)]
public sealed class SqliteMigrationSnapshotTests(SqliteEngineFixture fixture)
{
    /// <summary>
    ///     This class collects log messages with their level.
    /// </summary>
    private sealed class ListLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable BeginScope<TState>(TState state) => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        ) => Entries.Add((logLevel, formatter(state, exception)));
    }

    private StorageOptions Options(
        string connectionString,
        Action<StorageMigrationSafetyOptions> configure = null
    )
    {
        StorageOptions options = new()
        {
            Engine = fixture.Engine,
            ConnectionString = connectionString,
        };
        configure?.Invoke(options.MigrationSafety);
        return options;
    }

    private static string DatabasePath(string connectionString) =>
        new SqliteConnectionStringBuilder(connectionString).DataSource;

    private static string SnapshotDirectory(string connectionString) =>
        Path.Combine(
            Path.GetDirectoryName(DatabasePath(connectionString)),
            SqliteMigrationSnapshotStore.DirectoryName
        );

    private static async Task<List<MigrationSnapshotManifest>> ListAsync(string connectionString) =>
        [.. await SqliteMigrationSnapshotStore.TryCreate(connectionString).ListAsync(default)];

    // Creates a database one migration behind with one rewrite row, and returns its context.
    private static async Task<(NinlilDnsContext Context, List<string> Migrations)> OneBehindAsync(
        EngineDatabase database
    )
    {
        NinlilDnsContext context = database.CreateContext();
        List<string> migrations = [.. context.Database.GetMigrations()];
        await context.GetService<IMigrator>().MigrateAsync(migrations[^2]);
        await AddRewriteAsync(context, Guid.NewGuid());
        return (context, migrations);
    }

    private static Task AddRewriteAsync(NinlilDnsContext context, Guid id) =>
        context.Database.ExecuteSqlRawAsync(
            "INSERT INTO \"NinlilDnsRewrites\" (\"Id\", \"Domain\", \"Target\", \"IsDisabled\", "
                + $"\"CreatedAtUtc\") VALUES ('{id.ToString().ToUpperInvariant()}', 'a.example', "
                + "'192.0.2.1', 0, '2026-01-01 00:00:00')"
        );

    private static async Task<long> CountRewritesAsync(string connectionString)
    {
        await using SqliteConnection connection = new(connectionString + ";Pooling=False");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM \"NinlilDnsRewrites\"";
        return (long)await command.ExecuteScalarAsync();
    }

    /// <summary>
    ///     Pending migrations on a database that holds data produce one snapshot file and a
    ///     manifest with status Succeeded; the snapshot opened on its own has the old applied
    ///     list and the seeded row.
    /// </summary>
    [Fact]
    public async Task RunAsync_WithPendingMigrations_TakesASnapshotAndMarksItSucceeded()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync(
            createSchema: false
        );
        (NinlilDnsContext context, List<string> migrations) = await OneBehindAsync(database);

        await RelationalStartupMigrator.RunAsync(
            context,
            Options(database.ConnectionString),
            new ListLogger()
        );

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        MigrationSnapshotManifest manifest = Assert.Single(
            await ListAsync(database.ConnectionString)
        );
        Assert.Equal(MigrationSnapshotStatusEnum.Succeeded, manifest.Status);
        Assert.Equal(migrations[^1], manifest.TargetMigration);
        Assert.Equal(migrations[..^1], manifest.AppliedMigrationsBefore);
        Assert.True(File.Exists(manifest.Location));
        Assert.Equal(1, manifest.Tables.Single(t => t.Name == "NinlilDnsRewrites").Rows);

        // The snapshot file read on its own is the database as it was before the migration.
        string snapshotConnection = $"Data Source={manifest.Location};Mode=ReadOnly;Pooling=False";
        await using SqliteConnection connection = new(snapshotConnection);
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM \"__EFMigrationsHistory\"";
        Assert.Equal((long)migrations.Count - 1, (long)await command.ExecuteScalarAsync());
        command.CommandText = "SELECT COUNT(*) FROM \"NinlilDnsRewrites\"";
        Assert.Equal(1L, (long)await command.ExecuteScalarAsync());
    }

    /// <summary>
    ///     A fresh database and a database with nothing pending get no snapshot.
    /// </summary>
    [Fact]
    public async Task RunAsync_OnAFreshOrUpToDateDatabase_TakesNoSnapshot()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync(
            createSchema: false
        );
        NinlilDnsContext context = database.CreateContext();
        StorageOptions options = Options(database.ConnectionString);

        await RelationalStartupMigrator.RunAsync(context, options, new ListLogger());
        await RelationalStartupMigrator.RunAsync(context, options, new ListLogger());

        Assert.Empty(await ListAsync(database.ConnectionString));
    }

    /// <summary>
    ///     With <c>TakeSnapshot</c> off the migration runs, no file is written and a warning names
    ///     the setting.
    /// </summary>
    [Fact]
    public async Task RunAsync_WithTakeSnapshotOff_MigratesWithoutASnapshotAndWarns()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync(
            createSchema: false
        );
        (NinlilDnsContext context, _) = await OneBehindAsync(database);
        ListLogger logger = new();

        await RelationalStartupMigrator.RunAsync(
            context,
            Options(database.ConnectionString, safety => safety.TakeSnapshot = false),
            logger
        );

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.Empty(await ListAsync(database.ConnectionString));
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning && entry.Message.Contains("TAKE_SNAPSHOT")
        );
    }

    /// <summary>
    ///     When the snapshot directory cannot be created the start is refused with a message that
    ///     names the setting that skips snapshots, and the database is not migrated.
    /// </summary>
    [Fact]
    public async Task RunAsync_WhenTheSnapshotCannotBeWritten_RefusesAndMigratesNothing()
    {
        // File permissions do not stop root, and Windows has no Unix modes.
        if (OperatingSystem.IsWindows() || Environment.UserName == "root")
            return;

        await using EngineDatabase database = await fixture.CreateDatabaseAsync(
            createSchema: false
        );
        (NinlilDnsContext context, List<string> migrations) = await OneBehindAsync(database);
        // A directory without write permission stops the snapshot file from being created. The
        // directory is shared by this collection's tests, which run one after another.
        string directory = SnapshotDirectory(database.ConnectionString);
        Directory.CreateDirectory(directory);
        UnixFileMode original = File.GetUnixFileMode(directory);
        File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        DatabaseStartupRefusedException refusal;
        try
        {
            refusal = await Assert.ThrowsAsync<DatabaseStartupRefusedException>(() =>
                RelationalStartupMigrator.RunAsync(
                    context,
                    Options(database.ConnectionString),
                    new ListLogger()
                )
            );
        }
        finally
        {
            File.SetUnixFileMode(directory, original);
        }

        Assert.Contains("NINLIL_STORAGE_MIGRATION_SAFETY_TAKE_SNAPSHOT=false", refusal.Message);
        Assert.Equal(
            [migrations[^1]],
            (await context.Database.GetPendingMigrationsAsync()).ToList()
        );
    }

    /// <summary>
    ///     A Failed snapshot for this build's last migration refuses the start at once, without a
    ///     new snapshot and without migrating, and the message names the snapshot file and both
    ///     restore methods. With <c>RetryFailedMigration</c> the start reuses that snapshot,
    ///     migrates and marks it Succeeded.
    /// </summary>
    [Fact]
    public async Task RunAsync_WithAFailedMarker_RefusesUntilRetryIsSet()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync(
            createSchema: false
        );
        (NinlilDnsContext context, List<string> migrations) = await OneBehindAsync(database);
        SqliteMigrationSnapshotStore store = SqliteMigrationSnapshotStore.TryCreate(
            database.ConnectionString
        );
        MigrationSnapshotManifest marker = await store.CreateAsync(
            migrations[..^1],
            migrations[^1],
            default
        );
        await store.SetStatusAsync(
            marker,
            MigrationSnapshotStatusEnum.Failed,
            "disk on fire",
            default
        );

        DatabaseStartupRefusedException refusal =
            await Assert.ThrowsAsync<DatabaseStartupRefusedException>(() =>
                RelationalStartupMigrator.RunAsync(
                    context,
                    Options(database.ConnectionString),
                    new ListLogger()
                )
            );

        Assert.Contains(marker.Location, refusal.Message);
        Assert.Contains(
            "NINLIL_STORAGE_MIGRATION_SAFETY_RESTORE_SNAPSHOT=" + marker.Id,
            refusal.Message
        );
        Assert.Contains(DatabasePath(database.ConnectionString) + "-wal", refusal.Message);
        Assert.Contains(DatabasePath(database.ConnectionString) + "-shm", refusal.Message);
        Assert.Contains("RETRY_FAILED_MIGRATION", refusal.Message);
        Assert.Contains("disk on fire", refusal.Message);
        Assert.Single(await ListAsync(database.ConnectionString));
        Assert.Equal(
            [migrations[^1]],
            (await context.Database.GetPendingMigrationsAsync()).ToList()
        );

        await RelationalStartupMigrator.RunAsync(
            context,
            Options(database.ConnectionString, safety => safety.RetryFailedMigration = true),
            new ListLogger()
        );

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        MigrationSnapshotManifest after = Assert.Single(await ListAsync(database.ConnectionString));
        Assert.Equal(marker.Id, after.Id);
        Assert.Equal(MigrationSnapshotStatusEnum.Succeeded, after.Status);
    }

    /// <summary>
    ///     With <c>SnapshotsToKeep</c> 2, the third successful migration deletes the oldest
    ///     snapshot. Each run starts one migration behind by restoring the previous snapshot.
    /// </summary>
    [Fact]
    public async Task RunAsync_WithMoreSnapshotsThanToKeep_DeletesTheOldest()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync(
            createSchema: false
        );
        (NinlilDnsContext context, _) = await OneBehindAsync(database);
        string connectionString = database.ConnectionString;

        await RelationalStartupMigrator.RunAsync(
            context,
            Options(connectionString, safety => safety.SnapshotsToKeep = 2),
            new ListLogger()
        );
        string first = Assert.Single(await ListAsync(connectionString)).Id;

        string previous = first;
        List<string> ids = [first];
        for (int run = 0; run < 2; run++)
        {
            string restore = previous;
            await RelationalStartupMigrator.RunAsync(
                context,
                Options(
                    connectionString,
                    safety =>
                    {
                        safety.SnapshotsToKeep = 2;
                        safety.RestoreSnapshot = restore;
                    }
                ),
                new ListLogger()
            );
            previous = (await ListAsync(connectionString))
                .Select(manifest => manifest.Id)
                .Except(ids)
                .Single();
            ids.Add(previous);
        }

        List<MigrationSnapshotManifest> remaining = await ListAsync(connectionString);
        Assert.Equal(2, remaining.Count);
        Assert.DoesNotContain(remaining, manifest => manifest.Id == first);
        Assert.Equal(
            2,
            Directory.GetFiles(SnapshotDirectory(connectionString), "*.sqlite3").Length
        );
    }

    /// <summary>
    ///     The restore setting puts the snapshot's rows back and marks it Restored; a second run
    ///     with the setting still in place logs a warning and does not restore again.
    /// </summary>
    [Fact]
    public async Task RunAsync_WithRestoreSnapshot_RestoresOnceAndWarnsTheSecondTime()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync(
            createSchema: false
        );
        (NinlilDnsContext context, _) = await OneBehindAsync(database);
        string connectionString = database.ConnectionString;
        await RelationalStartupMigrator.RunAsync(
            context,
            Options(connectionString),
            new ListLogger()
        );
        string id = Assert.Single(await ListAsync(connectionString)).Id;
        await AddRewriteAsync(context, Guid.NewGuid());
        Assert.Equal(2, await CountRewritesAsync(connectionString));

        // The restore removes the second row; the migration that follows runs again.
        await RelationalStartupMigrator.RunAsync(
            context,
            Options(connectionString, safety => safety.RestoreSnapshot = id),
            new ListLogger()
        );

        Assert.Equal(1, await CountRewritesAsync(connectionString));
        List<MigrationSnapshotManifest> manifests = await ListAsync(connectionString);
        Assert.Equal(
            MigrationSnapshotStatusEnum.Restored,
            manifests.Single(m => m.Id == id).Status
        );
        Assert.Equal(2, manifests.Count);

        await AddRewriteAsync(context, Guid.NewGuid());
        ListLogger logger = new();
        await RelationalStartupMigrator.RunAsync(
            context,
            Options(connectionString, safety => safety.RestoreSnapshot = id),
            logger
        );

        Assert.Equal(2, await CountRewritesAsync(connectionString));
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning && entry.Message.Contains("already restored")
        );
    }

    /// <summary>
    ///     A restore setting that names no snapshot refuses the start and lists the ids that
    ///     exist.
    /// </summary>
    [Fact]
    public async Task RunAsync_WithAnUnknownRestoreSnapshot_Refuses()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync(
            createSchema: false
        );
        NinlilDnsContext context = database.CreateContext();

        DatabaseStartupRefusedException refusal =
            await Assert.ThrowsAsync<DatabaseStartupRefusedException>(() =>
                RelationalStartupMigrator.RunAsync(
                    context,
                    Options(
                        database.ConnectionString,
                        safety => safety.RestoreSnapshot = "20200101000000"
                    ),
                    new ListLogger()
                )
            );

        Assert.Contains("20200101000000", refusal.Message);
    }
}
