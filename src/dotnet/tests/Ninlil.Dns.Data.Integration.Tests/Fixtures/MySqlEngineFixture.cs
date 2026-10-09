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

    /// <inheritdoc />
    public override async Task<EngineDatabase> CreateLeastPrivilegeDatabaseAsync(
        bool createSchema = false
    )
    {
        // The statements follow src/script/database/mysql/init.sql: a login with all
        // privileges on its own database only.
        string name = $"ninlil_{Guid.NewGuid():N}";
        string user = name[..30];
        string password = $"pw-{Guid.NewGuid():N}";
        await using (MySqlConnection admin = new(BuildConnectionString("mysql")))
        {
            await admin.OpenAsync();
            await Execute(
                admin,
                $"CREATE DATABASE `{name}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci"
            );
            await Execute(admin, $"CREATE USER '{user}'@'%' IDENTIFIED BY '{password}'");
            await Execute(
                admin,
                $"GRANT ALL PRIVILEGES ON `{name.Replace("_", "\\_")}`.* TO '{user}'@'%'"
            );
        }

        string connectionString = new MySqlConnectionStringBuilder(BuildConnectionString(name))
        {
            UserID = user,
            Password = password,
        }.ConnectionString;
        return await BuildDatabaseAsync(connectionString, createSchema);
    }

    private static async Task Execute(MySqlConnection connection, string sql)
    {
        await using MySqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
