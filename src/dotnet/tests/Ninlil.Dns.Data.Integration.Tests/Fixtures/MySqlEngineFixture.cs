using MySqlConnector;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Options;
using Testcontainers.MySql;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Fixtures;

/// <summary>
///     This class runs one MySQL 8.4 container for the MySQL collection, pinned by digest. It
///     connects as <c>root</c> so that EF Core can create a database per test.
/// </summary>
public sealed class MySqlEngineFixture : EngineFixtureBase
{
    private readonly MySqlContainer _container = new MySqlBuilder(
        "public.ecr.aws/docker/library/mysql:8.4@sha256:6ea90827b1100f8f2ae306a539f86d2c264a26ed435a2a9f75551dd5c3aeb242"
    )
        .WithUsername("root")
        .WithPortBinding(MySqlBuilder.MySqlPort, assignRandomHostPort: true)
        .Build();

    /// <inheritdoc />
    public override StorageEngineEnum Engine => StorageEngineEnum.MySql;

    /// <inheritdoc />
    public override Task InitializeAsync() => _container.StartAsync();

    /// <inheritdoc />
    public override Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <inheritdoc />
    protected override string BuildConnectionString(string databaseName) =>
        new MySqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = databaseName,
        }.ConnectionString;

    /// <summary>
    ///     This method returns a connection string for the server's <c>mysql</c> system database
    ///     with pooling off, so every connection it opens is a new server connection (the
    ///     connection-count test relies on that).
    /// </summary>
    public string BuildNonPooledConnectionString() =>
        new MySqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Pooling = false,
        }.ConnectionString;
}
