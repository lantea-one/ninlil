using System.Data.Common;
using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Exceptions;
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Maintenance;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;
using Ninlil.Dns.Server.Backup;
using Ninlil.Dns.Server.Backup.Crypto;
using Ninlil.Dns.Server.Backup.Model;
using Ninlil.Dns.Server.Backup.Options;
using Ninlil.Dns.Server.Core.Certificates;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests;

/// <summary>
///     This class holds the tests of the in-database migration snapshots that run against each
///     server engine; one subclass per engine supplies the fixture. Every test runs on a database
///     created with a least-privilege account (the privileges of the scripts in
///     <c>src/script/database/*/init.sql</c>, see
///     <see cref="EngineFixtureBase.CreateLeastPrivilegeDatabaseAsync" />), not the container's
///     superuser, so a snapshot that needs more than the documented account would fail here. Each
///     test brings an empty database to the migration before the last one, seeds a zone with
///     records, a user with a role and a lease, and drives <see cref="RelationalStartupMigrator" />
///     or the store.
/// </summary>
public abstract class ServerMigrationSnapshotTestsBase(EngineFixtureBase fixture)
{
    private const string FailingMigration = "AddZoneValidateDnssec";

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

    /// <summary>
    ///     This property contains the storage engine under test.
    /// </summary>
    protected StorageEngineEnum Engine => fixture.Engine;

    private StorageOptions Options(
        EngineDatabase database,
        Action<StorageMigrationSafetyOptions> configure = null
    )
    {
        StorageOptions options = new()
        {
            Engine = fixture.Engine,
            ConnectionString = database.ConnectionString,
        };
        configure?.Invoke(options.MigrationSafety);
        return options;
    }

    private string Quote(string name) =>
        fixture.Engine switch
        {
            StorageEngineEnum.MySql => $"`{name}`",
            StorageEngineEnum.SqlServer => $"[{name}]",
            _ => $"\"{name}\"",
        };

    private static string TableOf<TEntity>(NinlilDnsContext context) =>
        context.Model.FindEntityType(typeof(TEntity)).GetTableName();

    private static IMigrationSnapshotStore StoreOf(
        StorageEngineEnum engine,
        NinlilDnsContext context
    ) => MigrationSnapshotStores.Create(engine, context);

    private async Task<long> ScalarAsync(NinlilDnsContext context, string sql)
    {
        DbConnection connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            await using DbCommand command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    private Task<long> CountAsync(NinlilDnsContext context, string table) =>
        ScalarAsync(context, $"SELECT COUNT(*) FROM {Quote(table)}");

    // The number of snapshots that exist as schemas (PostgreSQL, SQL Server) or as manifest
    // tables (MySQL), counted straight from the catalog.
    private Task<long> SnapshotContainerCountAsync(NinlilDnsContext context) =>
        ScalarAsync(
            context,
            fixture.Engine switch
            {
                StorageEngineEnum.PostgreSql =>
                    "SELECT COUNT(*) FROM information_schema.schemata WHERE schema_name LIKE 'ninlil\\_snapshot\\_%'",
                StorageEngineEnum.SqlServer =>
                    "SELECT COUNT(*) FROM sys.schemas WHERE name LIKE 'ninlil[_]snapshot[_]%'",
                _ =>
                    "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name LIKE 'nsnap\\_%\\_\\_manifest'",
            }
        );

    private Task<long> ForeignKeyCountAsync(NinlilDnsContext context) =>
        ScalarAsync(
            context,
            fixture.Engine switch
            {
                StorageEngineEnum.PostgreSql =>
                    "SELECT COUNT(*) FROM pg_constraint con JOIN pg_namespace ns ON ns.oid = con.connamespace WHERE con.contype = 'f' AND ns.nspname = current_schema()",
                StorageEngineEnum.SqlServer => "SELECT COUNT(*) FROM sys.foreign_keys",
                _ =>
                    "SELECT COUNT(*) FROM information_schema.referential_constraints WHERE constraint_schema = DATABASE()",
            }
        );

    /// <summary>
    ///     This method creates a least-privilege database one migration behind and seeds it.
    /// </summary>
    private async Task<(
        EngineDatabase Database,
        NinlilDnsContext Context,
        List<string> Migrations,
        Guid ZoneId
    )> OneBehindAsync(int behind = 1)
    {
        EngineDatabase database = await fixture.CreateLeastPrivilegeDatabaseAsync();
        NinlilDnsContext context = database.CreateContext();
        List<string> migrations = [.. context.Database.GetMigrations()];
        await context.GetService<IMigrator>().MigrateAsync(migrations[^(behind + 1)]);

        Guid zoneId = Guid.NewGuid();
        await using (AsyncServiceScope scope = database.Services.CreateAsyncScope())
        {
            IServiceProvider services = scope.ServiceProvider;
            Guid roleId = Guid.NewGuid();
            Guid userId = Guid.NewGuid();

            // On SQL Server the zone goes in with SQL that leaves out CloudSyncInterval: that
            // column is `time` before the ConvertTimeSpansToTicks migration and `bigint` after
            // it, so the repository cannot write it in both schemas. The column default fills it.
            if (fixture.Engine == StorageEngineEnum.SqlServer)
            {
                await ExecAsync(
                    context,
                    "INSERT INTO [NinlilDnsZones] ([Id], [CreatedAtUtc], [Name], [Type], "
                        + "[DenialOfExistenceMode], [DynamicUpdateEnabled], [IsDisabled], "
                        + "[IsDnssecSigned], [Nsec3Iterations], [TransferTransport], "
                        + $"[ValidateDnssec]) VALUES ('{zoneId}', SYSDATETIMEOFFSET(), "
                        + "'snap.example', 'Primary', 0, 0, 0, 0, 0, 0, 0)"
                );
            }
            else
            {
                await services
                    .GetRequiredService<IZoneRepository>()
                    .AddAsync(
                        new ZoneEntity
                        {
                            Id = zoneId,
                            Name = "snap.example",
                            Type = ZoneTypeEnum.Primary,
                            CreatedAtUtc = DateTimeOffset.UtcNow,
                        }
                    );
            }

            for (int index = 0; index < 3; index++)
                await services
                    .GetRequiredService<IRecordRepository>()
                    .AddAsync(
                        new RecordEntity
                        {
                            Id = Guid.NewGuid(),
                            ZoneId = zoneId,
                            Name = $"r{index}.snap.example",
                            Type = DnsResourceRecordTypeEnum.A,
                            DataJson = """{"Address":"192.0.2.1"}""",
                            CreatedAtUtc = DateTimeOffset.UtcNow,
                        }
                    );
            await services
                .GetRequiredService<IRoleRepository>()
                .AddAsync(
                    new RoleEntity
                    {
                        Id = roleId,
                        Name = "snap-role",
                        NormalizedName = "SNAP-ROLE",
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                    }
                );
            await services
                .GetRequiredService<IUserRepository>()
                .AddAsync(
                    new UserEntity
                    {
                        Id = userId,
                        UserName = "snap-user",
                        NormalizedUserName = "SNAP-USER",
                        PasswordHash = "not-a-real-hash",
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                    }
                );
            await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
            await services.GetRequiredService<IUserRepository>().AddToRoleAsync(userId, roleId);
            await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        // A lease in the format of the schema before the last migration: only the columns that
        // need no engine-specific literal besides the creation time.
        string created = fixture.Engine switch
        {
            StorageEngineEnum.PostgreSql => "'2026-01-01 00:00:00+00'",
            StorageEngineEnum.MySql => "'2026-01-01 00:00:00.000000'",
            _ => "'2026-01-01T00:00:00.0000000+00:00'",
        };
        string columns = string.Join(
            ", ",
            new[]
            {
                "Id",
                "ProtocolVersion",
                "ScopeName",
                "ClientIdentifier",
                "IpAddress",
                "State",
                "CreatedAtUtc",
            }.Select(Quote)
        );
        await ExecAsync(
            context,
            $"INSERT INTO {Quote("NinlilDhcpLeases")} ({columns}) VALUES ('{Guid.NewGuid()}', "
                + $"'IPv4', 'scope', 'client', '10.0.0.1', 'Offered', {created})"
        );
        return (database, context, migrations, zoneId);
    }

    // A plain string parameter keeps analyzer EF1002 quiet: every statement here is built from
    // constants and generated names, never from input.
    private static Task ExecAsync(NinlilDnsContext context, string sql) =>
        context.Database.ExecuteSqlRawAsync(sql);

    private Task RunAsync(
        NinlilDnsContext context,
        EngineDatabase database,
        ListLogger logger = null,
        Action<StorageMigrationSafetyOptions> configure = null
    ) =>
        RelationalStartupMigrator.RunAsync(
            context,
            Options(database, configure),
            logger ?? new ListLogger()
        );

    /// <summary>
    ///     Pending migrations on a seeded database give one snapshot with the right row counts
    ///     and status Succeeded, and the database is migrated.
    /// </summary>
    [Fact]
    public async Task RunAsync_WithPendingMigrations_TakesASnapshotWithRowCounts()
    {
        (EngineDatabase database, NinlilDnsContext context, List<string> migrations, _) =
            await OneBehindAsync();
        await using EngineDatabase owner = database;

        await RunAsync(context, database);

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        IMigrationSnapshotStore store = StoreOf(fixture.Engine, context);
        MigrationSnapshotManifest manifest = Assert.Single(await store.ListAsync(default));
        Assert.Equal(MigrationSnapshotStatusEnum.Succeeded, manifest.Status);
        Assert.Equal(fixture.Engine.ToString(), manifest.Engine);
        Assert.Equal(migrations[^1], manifest.TargetMigration);
        Assert.Equal(migrations[..^1], manifest.AppliedMigrationsBefore);
        Assert.Equal(1, manifest.Tables.Single(t => t.Name == TableOf<ZoneEntity>(context)).Rows);
        Assert.Equal(3, manifest.Tables.Single(t => t.Name == TableOf<RecordEntity>(context)).Rows);
        Assert.Equal(1, manifest.Tables.Single(t => t.Name == TableOf<RoleEntity>(context)).Rows);
        Assert.Equal(1, manifest.Tables.Single(t => t.Name == TableOf<UserEntity>(context)).Rows);
        Assert.Equal(1, manifest.Tables.Single(t => t.Name == TableOf<LeaseEntity>(context)).Rows);
        Assert.Equal(
            migrations.Count - 1,
            manifest.Tables.Single(t => t.Name == "__EFMigrationsHistory").Rows
        );
        Assert.Contains(manifest.Id, manifest.Location);
        Assert.Equal(1, await SnapshotContainerCountAsync(context));
    }

    /// <summary>
    ///     A fresh database and one with nothing pending get no snapshot.
    /// </summary>
    [Fact]
    public async Task RunAsync_OnAFreshOrUpToDateDatabase_TakesNoSnapshot()
    {
        EngineDatabase database = await fixture.CreateLeastPrivilegeDatabaseAsync();
        await using EngineDatabase owner = database;
        NinlilDnsContext context = database.CreateContext();

        await RunAsync(context, database);
        await RunAsync(context, database);

        Assert.Equal(0, await SnapshotContainerCountAsync(context));
    }

    /// <summary>
    ///     This method makes the <c>AddZoneValidateDnssec</c> migration fail by adding its column
    ///     first, from two migrations before it, and returns the refusal.
    /// </summary>
    private async Task<(
        EngineDatabase Database,
        NinlilDnsContext Context,
        DatabaseStartupRefusedException Refusal
    )> FailAsync()
    {
        EngineDatabase database = await fixture.CreateLeastPrivilegeDatabaseAsync();
        NinlilDnsContext context = database.CreateContext();
        List<string> migrations = [.. context.Database.GetMigrations()];
        int failing = migrations.FindIndex(name => name.EndsWith("_" + FailingMigration));
        await context.GetService<IMigrator>().MigrateAsync(migrations[failing - 2]);
        string booleanType = fixture.Engine switch
        {
            StorageEngineEnum.PostgreSql => "boolean",
            StorageEngineEnum.SqlServer => "bit",
            _ => "tinyint(1)",
        };
        await ExecAsync(
            context,
            $"ALTER TABLE {Quote("NinlilDnsZones")} ADD {Quote("ValidateDnssec")} {booleanType} NULL"
        );

        DatabaseStartupRefusedException refusal =
            await Assert.ThrowsAsync<DatabaseStartupRefusedException>(() =>
                RunAsync(context, database)
            );
        return (database, context, refusal);
    }

    /// <summary>
    ///     A failure that the engine rolled back leaves no snapshot behind; the refusal says the
    ///     database is unchanged. MySQL overrides this: its failure is not rolled back and the
    ///     snapshot stays as the Failed marker.
    /// </summary>
    [Fact]
    public virtual async Task RunAsync_WhenAMigrationFails_ReportsTheOutcomeAndTheSnapshot()
    {
        (
            EngineDatabase database,
            NinlilDnsContext context,
            DatabaseStartupRefusedException refusal
        ) = await FailAsync();
        await using EngineDatabase owner = database;

        Assert.Contains("was rolled back; the database is unchanged", refusal.Message);
        Assert.Empty(await StoreOf(fixture.Engine, context).ListAsync(default));
        Assert.Equal(0, await SnapshotContainerCountAsync(context));
    }

    /// <summary>
    ///     This method returns the refusal of a start that sees a Failed marker, to let the MySQL
    ///     test check that no new snapshot or migration follows it.
    /// </summary>
    protected async Task AssertFailedMarkerKeepsRefusingAsync()
    {
        (EngineDatabase database, NinlilDnsContext context, DatabaseStartupRefusedException first) =
            await FailAsync();
        await using EngineDatabase owner = database;
        IMigrationSnapshotStore store = StoreOf(fixture.Engine, context);

        MigrationSnapshotManifest marker = Assert.Single(await store.ListAsync(default));
        Assert.Equal(MigrationSnapshotStatusEnum.Failed, marker.Status);
        Assert.Contains("failed and was not rolled back", first.Message);
        Assert.Contains(marker.Id, first.Message);
        Assert.Contains("RESTORE_SNAPSHOT=" + marker.Id, first.Message);
        Assert.Contains("RETRY_FAILED_MIGRATION", first.Message);

        List<string> appliedAfterFailure = [.. await context.Database.GetAppliedMigrationsAsync()];
        DatabaseStartupRefusedException second =
            await Assert.ThrowsAsync<DatabaseStartupRefusedException>(() =>
                RunAsync(context, database)
            );

        Assert.Contains(marker.Id, second.Message);
        Assert.Single(await store.ListAsync(default));
        Assert.Equal(
            appliedAfterFailure,
            (await context.Database.GetAppliedMigrationsAsync()).ToList()
        );
    }

    /// <summary>
    ///     Restoring through the store brings back the old schema (the last migration is pending
    ///     again), every seeded row and the foreign keys, whatever happened to the data since;
    ///     running the restore a second time gives the same result.
    /// </summary>
    [Fact]
    public async Task RestoreAsync_PutsBackTheSchemaTheRowsAndTheForeignKeys_AlsoWhenRunTwice()
    {
        (EngineDatabase database, NinlilDnsContext context, List<string> migrations, Guid zoneId) =
            await OneBehindAsync();
        await using EngineDatabase owner = database;
        long foreignKeys = await ForeignKeyCountAsync(context);
        Assert.True(foreignKeys > 0);
        await RunAsync(context, database);
        IMigrationSnapshotStore store = StoreOf(fixture.Engine, context);
        MigrationSnapshotManifest manifest = Assert.Single(await store.ListAsync(default));

        // Change the data after the migration so the restore has something to undo.
        await ExecAsync(context, $"DELETE FROM {Quote("NinlilDnsRecords")}");

        for (int run = 0; run < 2; run++)
        {
            await store.RestoreAsync(manifest, default);
            context.ChangeTracker.Clear();

            Assert.Equal(
                [migrations[^1]],
                (await context.Database.GetPendingMigrationsAsync()).ToList()
            );
            foreach (MigrationSnapshotTable table in manifest.Tables)
                Assert.Equal(table.Rows, await CountAsync(context, table.Name));
            Assert.Equal(foreignKeys, await ForeignKeyCountAsync(context));
            Assert.Equal(
                MigrationSnapshotStatusEnum.Restored,
                Assert.Single(await store.ListAsync(default)).Status
            );

            // A record whose zone does not exist is refused by the restored foreign key.
            await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
            await scope
                .ServiceProvider.GetRequiredService<IRecordRepository>()
                .AddAsync(
                    new RecordEntity
                    {
                        Id = Guid.NewGuid(),
                        ZoneId = Guid.NewGuid(),
                        Name = "orphan.example",
                        Type = DnsResourceRecordTypeEnum.A,
                        DataJson = """{"Address":"192.0.2.9"}""",
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                    }
                );
            await Assert.ThrowsAnyAsync<Exception>(() =>
                scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync()
            );

            // Simulate an interrupted first run: the second run starts from damaged data.
            await ExecAsync(context, $"DELETE FROM {Quote("NinlilDnsRecords")} WHERE 1 = 1");
        }

        Assert.NotEqual(Guid.Empty, zoneId);
    }

    /// <summary>
    ///     The restore setting restores through the startup path: the database ends migrated,
    ///     the seeded rows are back, the snapshot is Restored and a second run with the setting
    ///     still set warns and does not restore again.
    /// </summary>
    [Fact]
    public async Task RunAsync_WithRestoreSnapshot_RestoresOnceAndWarnsTheSecondTime()
    {
        (EngineDatabase database, NinlilDnsContext context, _, _) = await OneBehindAsync();
        await using EngineDatabase owner = database;
        await RunAsync(context, database);
        IMigrationSnapshotStore store = StoreOf(fixture.Engine, context);
        string id = Assert.Single(await store.ListAsync(default)).Id;
        string records = TableOf<RecordEntity>(context);
        await ExecAsync(context, $"DELETE FROM {Quote(records)}");

        await RunAsync(context, database, configure: safety => safety.RestoreSnapshot = id);

        Assert.Equal(3, await CountAsync(context, records));
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.Equal(
            MigrationSnapshotStatusEnum.Restored,
            (await store.ListAsync(default)).Single(m => m.Id == id).Status
        );

        await ExecAsync(context, $"DELETE FROM {Quote(records)}");
        ListLogger logger = new();
        await RunAsync(context, database, logger, safety => safety.RestoreSnapshot = id);

        Assert.Equal(0, await CountAsync(context, records));
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning && entry.Message.Contains("already restored")
        );
    }

    /// <summary>
    ///     With <c>SnapshotsToKeep</c> 2 the third successful migration deletes the oldest
    ///     snapshot's schema or tables. Each run starts one migration behind by restoring the
    ///     previous snapshot.
    /// </summary>
    [Fact]
    public async Task RunAsync_WithMoreSnapshotsThanToKeep_DropsTheOldest()
    {
        (EngineDatabase database, NinlilDnsContext context, _, _) = await OneBehindAsync();
        await using EngineDatabase owner = database;
        IMigrationSnapshotStore store = StoreOf(fixture.Engine, context);
        Action<StorageMigrationSafetyOptions> keepTwo = safety => safety.SnapshotsToKeep = 2;

        await RunAsync(context, database, configure: keepTwo);
        string first = Assert.Single(await store.ListAsync(default)).Id;
        List<string> ids = [first];
        string previous = first;
        for (int run = 0; run < 2; run++)
        {
            string restore = previous;
            await RunAsync(
                context,
                database,
                configure: safety =>
                {
                    safety.SnapshotsToKeep = 2;
                    safety.RestoreSnapshot = restore;
                }
            );
            previous = (await store.ListAsync(default))
                .Select(manifest => manifest.Id)
                .Except(ids)
                .Single();
            ids.Add(previous);
        }

        IReadOnlyList<MigrationSnapshotManifest> remaining = await store.ListAsync(default);
        Assert.Equal(2, remaining.Count);
        Assert.DoesNotContain(remaining, manifest => manifest.Id == first);
        Assert.Equal(2, await SnapshotContainerCountAsync(context));
    }

    /// <summary>
    ///     A full-system backup counts the same rows with and without snapshots in the database:
    ///     the backup reads through the repositories, which never see snapshot schemas or tables.
    /// </summary>
    [Fact]
    public async Task CreateBackupAsync_IgnoresSnapshotSchemasAndTables()
    {
        (EngineDatabase database, NinlilDnsContext context, _, _) = await OneBehindAsync();
        await using EngineDatabase owner = database;
        await RunAsync(context, database);
        IMigrationSnapshotStore store = StoreOf(fixture.Engine, context);
        MigrationSnapshotManifest snapshot = Assert.Single(await store.ListAsync(default));

        string keyRing = Path.Combine(
            Path.GetTempPath(),
            $"ninlil-snap-keyring-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(keyRing);
        try
        {
            SystemBackupService backup = new(
                database.Services.GetRequiredService<IServiceScopeFactory>(),
                Microsoft.Extensions.Options.Options.Create(
                    new BackupOptions
                    {
                        DataProtectionKeyRingDirectory = keyRing,
                        MinimumPassphraseLength = 8,
                    }
                ),
                new TlsCertificateOptions
                {
                    PfxPath = "",
                    SelfSignedCertificateStatePath = Path.Combine(keyRing, "none.pfx"),
                },
                Options(database),
                NullLogger<SystemBackupService>.Instance
            );

            IReadOnlyDictionary<string, int> with = await EntityCountsAsync(backup);
            await store.DeleteAsync(snapshot, default);
            Assert.Equal(0, await SnapshotContainerCountAsync(context));
            IReadOnlyDictionary<string, int> without = await EntityCountsAsync(backup);

            Assert.Equal(without.OrderBy(p => p.Key), with.OrderBy(p => p.Key));
            Assert.Contains(with, pair => pair.Value > 0);
        }
        finally
        {
            Directory.Delete(keyRing, recursive: true);
        }
    }

    private static async Task<IReadOnlyDictionary<string, int>> EntityCountsAsync(
        IBackupService backup
    )
    {
        byte[] archive = BackupArchiveCodec.Unwrap(await backup.CreateBackupAsync(null));
        using ZipArchive zip = new(new MemoryStream(archive));
        using StreamReader reader = new(zip.GetEntry("manifest.json")!.Open());
        return Ninlil
            .Common.Serializer.JsonSerializer.Deserialize<BackupManifest>(
                await reader.ReadToEndAsync()
            )
            .EntityCounts;
    }
}
