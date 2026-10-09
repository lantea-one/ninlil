using Microsoft.Data.Sqlite;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.EngineHosts;

/// <summary>
///     This class provides SQLite databases. SQLite is a file, so there is no container and the
///     tests that use it do not need Docker. Each database is its own file in the host's
///     directory. Without an explicit directory the host uses a temporary one and deletes it on
///     dispose; with one (the benchmarks keep their seeded data there) it leaves the files.
/// </summary>
/// <param name="directory">
///     The directory for the database files, or null for a temporary directory that is deleted
///     on dispose.
/// </param>
public sealed class SqliteEngineHost(string directory = null) : EngineHost
{
    private readonly bool _isTemporary = directory == null;

    private readonly string _directory =
        directory ?? Path.Combine(Path.GetTempPath(), $"ninlil-integration-{Guid.NewGuid():N}");

    /// <inheritdoc />
    public override StorageEngineEnum Engine => StorageEngineEnum.Sqlite;

    /// <inheritdoc />
    public override Task StartAsync()
    {
        Directory.CreateDirectory(_directory);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override ValueTask DisposeAsync()
    {
        // Pooled connections keep the files open.
        SqliteConnection.ClearAllPools();

        if (_isTemporary)
            Directory.Delete(_directory, recursive: true);

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public override string BuildConnectionString(string databaseName) =>
        $"Data Source={Path.Combine(_directory, databaseName + ".sqlite3")}";
}
