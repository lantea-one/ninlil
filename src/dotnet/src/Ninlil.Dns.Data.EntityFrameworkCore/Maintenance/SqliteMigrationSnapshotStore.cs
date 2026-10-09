using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Maintenance;

/// <summary>
///     This class stores migration snapshots of a SQLite database as files. A snapshot is a copy
///     made with SQLite's online backup API (safe while the database is open, and in WAL mode),
///     placed in <c>migration-snapshots</c> next to the database as
///     <c>&lt;database name&gt;-&lt;id&gt;.sqlite3</c> with a sidecar <c>.json</c> manifest. The
///     files get mode 0600 on Unix, like the data directory. A restore copies the snapshot back
///     over the database file with the same API. An in-memory database has no file to copy, so
///     <see cref="TryCreate" /> returns null for it.
/// </summary>
public sealed class SqliteMigrationSnapshotStore : IMigrationSnapshotStore
{
    /// <summary>
    ///     This constant contains the name of the directory, next to the database file, that holds
    ///     the snapshots.
    /// </summary>
    public const string DirectoryName = "migration-snapshots";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string connectionString;
    private readonly string databasePath;
    private readonly string databaseName;
    private readonly string directory;

    private SqliteMigrationSnapshotStore(string connectionString, string databasePath)
    {
        this.connectionString = connectionString;
        this.databasePath = databasePath;
        databaseName = Path.GetFileNameWithoutExtension(databasePath);
        directory = Path.Combine(Path.GetDirectoryName(databasePath), DirectoryName);
    }

    /// <summary>
    ///     This method returns a store for the database in <paramref name="connectionString" />,
    ///     or null when that database is not a plain file (in-memory or a <c>file:</c> URI).
    /// </summary>
    public static SqliteMigrationSnapshotStore TryCreate(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return null;

        SqliteConnectionStringBuilder builder = new(connectionString);
        string source = builder.DataSource;
        if (
            string.IsNullOrWhiteSpace(source)
            || builder.Mode == SqliteOpenMode.Memory
            || source.Contains(":memory:", StringComparison.OrdinalIgnoreCase)
            || source.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
        )
            return null;

        return new SqliteMigrationSnapshotStore(connectionString, Path.GetFullPath(source));
    }

    private string SnapshotPath(string id) =>
        Path.Combine(directory, $"{databaseName}-{id}.sqlite3");

    private string ManifestPath(string id) => Path.Combine(directory, $"{databaseName}-{id}.json");

    // A connection that is not pooled, so closing it releases the file at once.
    private SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        SqliteConnectionStringBuilder builder = new(connectionString)
        {
            DataSource = path,
            Mode = mode,
            Pooling = false,
        };
        SqliteConnection connection = new(builder.ToString());
        connection.Open();
        return connection;
    }

    /// <inheritdoc />
    public async Task<MigrationSnapshotManifest> CreateAsync(
        IReadOnlyList<string> appliedMigrationsBefore,
        string targetMigration,
        CancellationToken stoppingToken
    )
    {
        Directory.CreateDirectory(directory);

        DateTime created = DateTime.UtcNow;
        string id = Id(created);
        while (File.Exists(SnapshotPath(id)) || File.Exists(ManifestPath(id)))
        {
            created = created.AddSeconds(1);
            id = Id(created);
        }

        string snapshotPath = SnapshotPath(id);
        try
        {
            // Create the file private before any data lands in it.
            FileStreamOptions fileOptions = new()
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
            };
            if (!OperatingSystem.IsWindows())
                fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (new FileStream(snapshotPath, fileOptions)) { }

            using (SqliteConnection source = Open(databasePath, SqliteOpenMode.ReadWrite))
            using (SqliteConnection target = Open(snapshotPath, SqliteOpenMode.ReadWrite))
                source.BackupDatabase(target);

            MigrationSnapshotManifest manifest = new()
            {
                Id = id,
                Engine = StorageEngineEnum.Sqlite.ToString(),
                CreatedUtc = created,
                ApplicationVersion = ApplicationVersion(),
                AppliedMigrationsBefore = [.. appliedMigrationsBefore],
                TargetMigration = targetMigration,
                Tables = CountTables(snapshotPath),
                Status = MigrationSnapshotStatusEnum.Pending,
                StatusUtc = created,
                Location = snapshotPath,
            };
            await WriteManifestAsync(manifest, stoppingToken);
            return manifest;
        }
        catch
        {
            File.Delete(snapshotPath);
            File.Delete(ManifestPath(id));
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
        if (!Directory.Exists(directory))
            return manifests;

        Regex name = ManifestFileName(databaseName);
        foreach (string path in Directory.GetFiles(directory, "*.json"))
        {
            Match match = name.Match(Path.GetFileName(path));
            if (!match.Success)
                continue;

            MigrationSnapshotManifest manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<MigrationSnapshotManifest>(
                    await File.ReadAllTextAsync(path, stoppingToken),
                    JsonOptions
                );
            }
            catch (JsonException)
            {
                // A damaged manifest cannot be trusted, so the snapshot is not offered.
                continue;
            }

            if (manifest?.Id == match.Groups["id"].Value)
                manifests.Add(manifest);
        }

        return [.. manifests.OrderBy(manifest => manifest.Id, StringComparer.Ordinal)];
    }

    /// <inheritdoc />
    public Task DeleteAsync(MigrationSnapshotManifest manifest, CancellationToken stoppingToken)
    {
        // The manifest goes first: a snapshot without a manifest is never offered, a manifest
        // without a snapshot would be.
        File.Delete(ManifestPath(manifest.Id));
        File.Delete(SnapshotPath(manifest.Id));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task RestoreAsync(
        MigrationSnapshotManifest manifest,
        CancellationToken stoppingToken
    )
    {
        string snapshotPath = SnapshotPath(manifest.Id);
        if (!File.Exists(snapshotPath))
            throw new FileNotFoundException(
                $"The snapshot file {snapshotPath} is missing.",
                snapshotPath
            );

        string databaseDirectory = Path.GetDirectoryName(databasePath);
        Directory.CreateDirectory(databaseDirectory);

        // Pooled connections of this process may still hold the old file.
        SqliteConnection.ClearAllPools();
        using (SqliteConnection source = Open(snapshotPath, SqliteOpenMode.ReadOnly))
        using (SqliteConnection target = Open(databasePath, SqliteOpenMode.ReadWriteCreate))
            source.BackupDatabase(target);
        SqliteConnection.ClearAllPools();

        // The restore must give back exactly what the snapshot held.
        List<MigrationSnapshotTable> restored = CountTables(databasePath);
        foreach (MigrationSnapshotTable expected in manifest.Tables)
        {
            MigrationSnapshotTable actual = restored.FirstOrDefault(table =>
                table.Name == expected.Name
            );
            if (actual == null || actual.Rows != expected.Rows)
                throw new InvalidOperationException(
                    $"After the restore, table {expected.Name} holds {actual?.Rows.ToString(CultureInfo.InvariantCulture) ?? "no"} "
                        + $"rows; the snapshot held {expected.Rows}."
                );
        }

        await SetStatusAsync(manifest, MigrationSnapshotStatusEnum.Restored, null, stoppingToken);
    }

    /// <inheritdoc />
    public string DescribeRestore(MigrationSnapshotManifest manifest) =>
        $"Snapshot {manifest.Id} is the file {SnapshotPath(manifest.Id)}. Restore it in one of two "
        + $"ways. Either set NINLIL_STORAGE_MIGRATION_SAFETY_RESTORE_SNAPSHOT={manifest.Id} (or pass "
        + $"--Storage:MigrationSafety:RestoreSnapshot={manifest.Id}), start Ninlil, and remove the "
        + "setting afterwards. Or stop Ninlil, copy the snapshot file over the database file "
        + $"{databasePath}, delete {databasePath}-wal and {databasePath}-shm if they exist, delete "
        + $"the manifest {ManifestPath(manifest.Id)}, and start Ninlil.";

    private async Task WriteManifestAsync(
        MigrationSnapshotManifest manifest,
        CancellationToken stoppingToken
    )
    {
        string path = ManifestPath(manifest.Id);
        FileStreamOptions fileOptions = new() { Mode = FileMode.Create, Access = FileAccess.Write };
        if (!OperatingSystem.IsWindows())
            fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        await using FileStream stream = new(path, fileOptions);
        await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, stoppingToken);
    }

    // Lists every user table of the database in the file with its row count.
    private List<MigrationSnapshotTable> CountTables(string path)
    {
        List<MigrationSnapshotTable> tables = [];
        using SqliteConnection connection = Open(path, SqliteOpenMode.ReadOnly);
        List<string> names = [];
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText =
                "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite\\_%' ESCAPE '\\' ORDER BY name";
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
                names.Add(reader.GetString(0));
        }

        foreach (string name in names)
        {
            using SqliteCommand count = connection.CreateCommand();
            count.CommandText = $"SELECT COUNT(*) FROM \"{name.Replace("\"", "\"\"")}\"";
            tables.Add(
                new MigrationSnapshotTable
                {
                    Name = name,
                    Rows = Convert.ToInt64(count.ExecuteScalar(), CultureInfo.InvariantCulture),
                }
            );
        }

        return tables;
    }

    private static string Id(DateTime utc) =>
        utc.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

    private static string ApplicationVersion() =>
        typeof(SqliteMigrationSnapshotStore)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

    private static Regex ManifestFileName(string databaseName) =>
        new(
            $"^{Regex.Escape(databaseName)}-(?<id>[0-9]{{14}})\\.json$",
            RegexOptions.CultureInvariant
        );
}
