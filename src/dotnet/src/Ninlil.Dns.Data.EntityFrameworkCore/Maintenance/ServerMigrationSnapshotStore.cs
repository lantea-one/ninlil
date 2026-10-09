using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Maintenance;

/// <summary>
///     This abstract class holds the algorithm shared by the migration snapshot stores of the
///     server engines (<see cref="PostgreSqlMigrationSnapshotStore" />,
///     <see cref="MySqlMigrationSnapshotStore" /> and <see cref="SqlServerMigrationSnapshotStore" />).
///     A snapshot lives inside the database itself, so it needs only the privileges the migrations
///     need anyway (the documented accounts in <c>src/script/database/*/init.sql</c>) and no dump
///     tool. A subclass supplies the engine-specific parts: where a snapshot's tables live (a
///     schema, or a table-name prefix), how to copy a table and how to list and drop a snapshot.
///     A snapshot copies every base table of the main schema, read from the catalog because the
///     schema can be older than the entity model. A restore drops the Ninlil tables, rebuilds the
///     old schema with the migrator (which brings back indexes and foreign keys), and copies the
///     rows back in foreign-key order. It is not atomic (EF Core refuses to migrate inside a user
///     transaction) but it can be run again: every run starts by dropping the tables.
/// </summary>
internal abstract class ServerMigrationSnapshotStore : IMigrationSnapshotStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly NinlilDnsContext context;
    private readonly StorageEngineEnum engine;

    /// <summary>
    ///     This constructor stores the context whose database is snapshotted and the engine.
    /// </summary>
    protected ServerMigrationSnapshotStore(NinlilDnsContext context, StorageEngineEnum engine)
    {
        this.context = context;
        this.engine = engine;
        Catalog = new RelationalCatalog(context, engine);
    }

    /// <summary>
    ///     This property contains the catalog helper for the database.
    /// </summary>
    protected RelationalCatalog Catalog { get; }

    /// <summary>
    ///     This property contains the column type for short text (ids, status names).
    /// </summary>
    protected abstract string ShortTextType { get; }

    /// <summary>
    ///     This property contains the column type for the manifest JSON.
    /// </summary>
    protected abstract string LongTextType { get; }

    /// <summary>
    ///     This method creates the empty container of snapshot <paramref name="id" /> (a schema;
    ///     MySQL has nothing to create).
    /// </summary>
    protected abstract Task CreateContainerAsync(string id, CancellationToken stoppingToken);

    /// <summary>
    ///     This method copies one table of the main schema into the snapshot, rows included.
    /// </summary>
    protected abstract Task CopyTableAsync(
        string id,
        string table,
        CancellationToken stoppingToken
    );

    /// <summary>
    ///     This method returns the quoted name, schema-qualified where the engine has schemas, of
    ///     the copy of <paramref name="table" /> in snapshot <paramref name="id" />.
    /// </summary>
    protected abstract string SnapshotTableReference(string id, string table);

    /// <summary>
    ///     This method returns the ids of every snapshot container in the database, whether or
    ///     not it has a manifest.
    /// </summary>
    protected abstract Task<List<string>> ListSnapshotIdsAsync(CancellationToken stoppingToken);

    /// <summary>
    ///     This method returns the original names of the tables copied into snapshot
    ///     <paramref name="id" />, without the manifest table.
    /// </summary>
    protected abstract Task<List<string>> ListSnapshotTablesAsync(
        string id,
        CancellationToken stoppingToken
    );

    /// <summary>
    ///     This method returns the column names of the copy of <paramref name="table" />.
    /// </summary>
    protected abstract Task<List<string>> ListSnapshotColumnsAsync(
        string id,
        string table,
        CancellationToken stoppingToken
    );

    /// <summary>
    ///     This method returns the quoted name of the manifest table of snapshot
    ///     <paramref name="id" />.
    /// </summary>
    protected abstract string ManifestTableReference(string id);

    /// <summary>
    ///     This method removes snapshot <paramref name="id" />, its manifest included.
    /// </summary>
    protected abstract Task DropSnapshotAsync(string id, CancellationToken stoppingToken);

    /// <summary>
    ///     This method describes where snapshot <paramref name="id" /> lives, in words an
    ///     operator can act on.
    /// </summary>
    protected abstract string DescribeLocation(string id);

    /// <inheritdoc />
    public async Task<MigrationSnapshotManifest> CreateAsync(
        IReadOnlyList<string> appliedMigrationsBefore,
        string targetMigration,
        CancellationToken stoppingToken
    )
    {
        HashSet<string> existing = [.. await ListSnapshotIdsAsync(stoppingToken)];
        DateTime created = DateTime.UtcNow;
        string id = Id(created);
        while (existing.Contains(id))
        {
            created = created.AddSeconds(1);
            id = Id(created);
        }

        try
        {
            await CreateContainerAsync(id, stoppingToken);
            List<MigrationSnapshotTable> tables = [];
            foreach (string table in await Catalog.ListTablesAsync(null, stoppingToken))
            {
                await CopyTableAsync(id, table, stoppingToken);
                tables.Add(
                    new MigrationSnapshotTable
                    {
                        Name = table,
                        Rows = await CountAsync(SnapshotTableReference(id, table), stoppingToken),
                    }
                );
            }

            MigrationSnapshotManifest manifest = new()
            {
                Id = id,
                Engine = engine.ToString(),
                CreatedUtc = created,
                ApplicationVersion = ApplicationVersion(),
                AppliedMigrationsBefore = [.. appliedMigrationsBefore],
                TargetMigration = targetMigration,
                Tables = tables,
                Status = MigrationSnapshotStatusEnum.Pending,
                StatusUtc = created,
                Location = DescribeLocation(id),
            };
            await Catalog.ExecuteAsync(
                $"CREATE TABLE {ManifestTableReference(id)} ({Catalog.Quote("Id")} {ShortTextType} NOT NULL PRIMARY KEY, "
                    + $"{Catalog.Quote("Status")} {ShortTextType} NOT NULL, "
                    + $"{Catalog.Quote("Manifest")} {LongTextType} NOT NULL)",
                stoppingToken
            );
            await WriteManifestAsync(manifest, stoppingToken);
            return manifest;
        }
        catch
        {
            try
            {
                await DropSnapshotAsync(id, CancellationToken.None);
            }
            catch (Exception)
            {
                // The original failure is the one to report.
            }

            throw;
        }
    }

    /// <inheritdoc />
    public async Task SetStatusAsync(
        MigrationSnapshotManifest manifest,
        MigrationSnapshotStatusEnum status,
        string failure,
        CancellationToken stoppingToken
    )
    {
        manifest.Status = status;
        manifest.StatusUtc = DateTime.UtcNow;
        manifest.Failure = failure;
        await WriteManifestAsync(manifest, stoppingToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MigrationSnapshotManifest>> ListAsync(
        CancellationToken stoppingToken
    )
    {
        List<MigrationSnapshotManifest> manifests = [];
        foreach (string id in await ListSnapshotIdsAsync(stoppingToken))
        {
            List<string[]> rows;
            try
            {
                rows = await Catalog.QueryAsync(
                    $"SELECT {Catalog.Quote("Manifest")} FROM {ManifestTableReference(id)}",
                    stoppingToken
                );
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A snapshot whose creation was cut short has no manifest table; it is not offered.
                continue;
            }

            if (rows.Count != 1)
                continue;

            try
            {
                MigrationSnapshotManifest manifest =
                    JsonSerializer.Deserialize<MigrationSnapshotManifest>(rows[0][0], JsonOptions);
                if (manifest?.Id == id)
                    manifests.Add(manifest);
            }
            catch (JsonException)
            {
                // A damaged manifest cannot be trusted, so the snapshot is not offered.
            }
        }

        return [.. manifests.OrderBy(manifest => manifest.Id, StringComparer.Ordinal)];
    }

    /// <inheritdoc />
    public Task DeleteAsync(MigrationSnapshotManifest manifest, CancellationToken stoppingToken) =>
        DropSnapshotAsync(manifest.Id, stoppingToken);

    /// <inheritdoc />
    public async Task RestoreAsync(
        MigrationSnapshotManifest manifest,
        CancellationToken stoppingToken
    )
    {
        // Step 1: a snapshot restores only into the engine that took it.
        if (manifest.Engine != engine.ToString())
            throw new InvalidOperationException(
                $"Snapshot {manifest.Id} was taken from a {manifest.Engine} database."
            );
        if (manifest.AppliedMigrationsBefore.Count == 0)
            throw new InvalidOperationException(
                $"Snapshot {manifest.Id} records no applied migrations, so the schema cannot be rebuilt."
            );

        List<string> snapshotTables = await ListSnapshotTablesAsync(manifest.Id, stoppingToken);
        foreach (MigrationSnapshotTable expected in manifest.Tables)
            if (!snapshotTables.Contains(expected.Name))
                throw new InvalidOperationException(
                    $"The snapshot copy of table {expected.Name} is missing."
                );

        // Step 2: drop the foreign keys of, then drop, every table the snapshot or the current
        // model owns, and no other table.
        HashSet<string> owned = [.. snapshotTables];
        owned.Add(RelationalCatalog.HistoryTable);
        foreach (string name in context.Model.GetEntityTypes().Select(t => t.GetTableName()))
            if (name != null)
                owned.Add(name);
        HashSet<string> present = [.. await Catalog.ListTablesAsync(null, stoppingToken)];
        owned.IntersectWith(present);

        foreach (
            (string table, string referenced, string name) in await Catalog.ListForeignKeysAsync(
                stoppingToken
            )
        )
            if (owned.Contains(table))
                await Catalog.DropForeignKeyAsync(table, name, stoppingToken);
        foreach (string table in owned)
            await Catalog.ExecuteAsync($"DROP TABLE {Catalog.Quote(table)}", stoppingToken);

        // Step 3: the migrator rebuilds the exact old schema with its indexes and foreign keys.
        context.ChangeTracker.Clear();
        await context
            .GetService<IMigrator>()
            .MigrateAsync(manifest.AppliedMigrationsBefore[^1], stoppingToken);

        // Step 4: copy the rows back, parents before children.
        List<string> targets =
        [
            .. (await Catalog.ListTablesAsync(null, stoppingToken)).Where(table =>
                table != RelationalCatalog.HistoryTable && snapshotTables.Contains(table)
            ),
        ];
        foreach (
            string table in RelationalCatalog.OrderByForeignKeys(
                targets,
                await Catalog.ListForeignKeysAsync(stoppingToken)
            )
        )
        {
            string columns = string.Join(
                ", ",
                (await ListSnapshotColumnsAsync(manifest.Id, table, stoppingToken)).Select(
                    Catalog.Quote
                )
            );
            await Catalog.ExecuteAsync(
                $"INSERT INTO {Catalog.Quote(table)} ({columns}) SELECT {columns} FROM {SnapshotTableReference(manifest.Id, table)}",
                stoppingToken
            );
        }

        // Step 5: the restore must give back exactly what the snapshot held.
        List<string> appliedNow =
        [
            .. (
                await Catalog.QueryAsync(
                    $"SELECT {Catalog.Quote("MigrationId")} FROM {Catalog.Quote(RelationalCatalog.HistoryTable)}",
                    stoppingToken
                )
            )
                .Select(row => row[0])
                .OrderBy(name => name, StringComparer.Ordinal),
        ];
        if (!appliedNow.SequenceEqual(manifest.AppliedMigrationsBefore, StringComparer.Ordinal))
            throw new InvalidOperationException(
                "After the restore, the applied migrations are "
                    + $"{string.Join(", ", appliedNow)}; the snapshot recorded "
                    + $"{string.Join(", ", manifest.AppliedMigrationsBefore)}."
            );
        foreach (MigrationSnapshotTable expected in manifest.Tables)
        {
            if (expected.Name == RelationalCatalog.HistoryTable)
                continue;
            long actual = await CountAsync(Catalog.Quote(expected.Name), stoppingToken);
            if (actual != expected.Rows)
                throw new InvalidOperationException(
                    $"After the restore, table {expected.Name} holds "
                        + $"{actual.ToString(CultureInfo.InvariantCulture)} rows; the snapshot held {expected.Rows}."
                );
        }

        // Step 6.
        await SetStatusAsync(manifest, MigrationSnapshotStatusEnum.Restored, null, stoppingToken);
    }

    /// <inheritdoc />
    public string DescribeRestore(MigrationSnapshotManifest manifest) =>
        $"Snapshot {manifest.Id} is kept in the database ({manifest.Location}). Ninlil restores it "
        + $"itself: set NINLIL_STORAGE_MIGRATION_SAFETY_RESTORE_SNAPSHOT={manifest.Id} (or pass "
        + $"--Storage:MigrationSafety:RestoreSnapshot={manifest.Id}), start Ninlil, and remove the "
        + "setting afterwards. The restore drops the Ninlil tables, rebuilds the schema as it "
        + "was and copies the rows back; if it is interrupted, start again with the same "
        + "setting.";

    private async Task WriteManifestAsync(
        MigrationSnapshotManifest manifest,
        CancellationToken stoppingToken
    )
    {
        string json = JsonSerializer.Serialize(manifest, JsonOptions);
        string table = ManifestTableReference(manifest.Id);
        int updated = await Catalog.ExecuteAsync(
            $"UPDATE {table} SET {Catalog.Quote("Status")} = @status, {Catalog.Quote("Manifest")} = @json "
                + $"WHERE {Catalog.Quote("Id")} = @id",
            stoppingToken,
            ("status", manifest.Status.ToString()),
            ("json", json),
            ("id", manifest.Id)
        );
        if (updated == 0)
            await Catalog.ExecuteAsync(
                $"INSERT INTO {table} ({Catalog.Quote("Id")}, {Catalog.Quote("Status")}, {Catalog.Quote("Manifest")}) "
                    + "VALUES (@id, @status, @json)",
                stoppingToken,
                ("id", manifest.Id),
                ("status", manifest.Status.ToString()),
                ("json", json)
            );
    }

    private async Task<long> CountAsync(string tableReference, CancellationToken stoppingToken) =>
        Convert.ToInt64(
            (await Catalog.QueryAsync($"SELECT COUNT(*) FROM {tableReference}", stoppingToken))[0][
                0
            ],
            CultureInfo.InvariantCulture
        );

    private static string Id(DateTime utc) =>
        utc.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

    private static string ApplicationVersion() =>
        typeof(ServerMigrationSnapshotStore)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
}
