using MySqlConnector;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Options;
using Testcontainers.MySql;

// Define our namespace.
namespace Ninlil.Dns.Data.EngineHosts;

/// <summary>
///     This class runs one MySQL 8.4 container for a MySQL host, pinned by digest. It
///     connects as <c>root</c> so that EF Core can create a database per test.
/// </summary>
public sealed class MySqlEngineHost : EngineHost
{
    /// <summary>
    ///     This constant contains the pinned image, shared with
    ///     <see cref="MySqlReplicatedEngineHost" /> so one digest line serves both.
    /// </summary>
    internal const string Image =
        "public.ecr.aws/docker/library/mysql:8.4@sha256:a0225d1ea6631c8bc2385dc4c2faba17bbf1834ecf369e1659a9a382bd8d5dd7";

    private readonly MySqlContainer _container = new MySqlBuilder(Image)
        .WithUsername("root")
        .WithPortBinding(MySqlBuilder.MySqlPort, assignRandomHostPort: true)
        .Build();

    /// <inheritdoc />
    public override StorageEngineEnum Engine => StorageEngineEnum.MySql;

    /// <inheritdoc />
    public override Task StartAsync() => _container.StartAsync();

    /// <inheritdoc />
    public override ValueTask DisposeAsync() => _container.DisposeAsync();

    /// <inheritdoc />
    public override string BuildConnectionString(string databaseName) =>
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
