using Microsoft.Data.Sqlite;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Fixtures;

/// <summary>
///     This class provides the SQLite collection. SQLite is a file, so there is no container and
///     the tests that use it do not need Docker (they carry no <c>RequiresDocker</c> trait). Each
///     per-test database is its own file in a temporary directory that is deleted on dispose.
/// </summary>
public sealed class SqliteEngineFixture : EngineFixtureBase
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-integration-{Guid.NewGuid():N}"
    );

    /// <inheritdoc />
    public override StorageEngineEnum Engine => StorageEngineEnum.Sqlite;

    /// <inheritdoc />
    public override Task InitializeAsync()
    {
        Directory.CreateDirectory(_directory);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task DisposeAsync()
    {
        // Pooled connections keep the files open.
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override string BuildConnectionString(string databaseName) =>
        $"Data Source={Path.Combine(_directory, databaseName + ".sqlite3")}";
}
