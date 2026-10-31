using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Options;
using Npgsql;
using Testcontainers.PostgreSql;

// Define our namespace.
namespace Ninlil.Dns.Data.EngineHosts;

/// <summary>
///     This class runs one PostgreSQL 16 container for a PostgreSQL host. The image is
///     the one <c>Ninlil.Dns.Server.QueryLog.Tests</c> uses, pinned by digest.
/// </summary>
public sealed class PostgreSqlEngineHost : EngineHost
{
    /// <summary>
    ///     This constant contains the pinned image, shared with
    ///     <see cref="PostgreSqlReplicatedEngineHost" /> so one digest line serves both.
    /// </summary>
    internal const string Image =
        "postgres:16-alpine@sha256:0cefaabd882e42bcb2a2f3dc56d3ce4770560ba62fd7f197f1a2e008fd5731ab";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(Image)
        .WithPortBinding(PostgreSqlBuilder.PostgreSqlPort, assignRandomHostPort: true)
        .Build();

    /// <inheritdoc />
    public override StorageEngineEnum Engine => StorageEngineEnum.PostgreSql;

    /// <inheritdoc />
    public override Task StartAsync() => _container.StartAsync();

    /// <inheritdoc />
    public override ValueTask DisposeAsync() => _container.DisposeAsync();

    /// <inheritdoc />
    public override string BuildConnectionString(string databaseName) =>
        new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = databaseName,
        }.ConnectionString;

    /// <inheritdoc />
    public override async Task<EngineDatabase> CreateLeastPrivilegeDatabaseAsync(
        bool createSchema = false
    )
    {
        // The statements follow src/script/database/postgresql/init.sql: a login that cannot
        // create databases or roles, owning its own database.
        string name = $"ninlil_{Guid.NewGuid():N}";
        string password = $"pw-{Guid.NewGuid():N}";
        await using (NpgsqlConnection admin = new(BuildConnectionString("postgres")))
        {
            await admin.OpenAsync();
            await Execute(admin, $"CREATE ROLE \"{name}\" WITH LOGIN PASSWORD '{password}'");
            await Execute(
                admin,
                $"CREATE DATABASE \"{name}\" WITH OWNER = \"{name}\" ENCODING = 'UTF8'"
            );
        }

        string connectionString = new NpgsqlConnectionStringBuilder(BuildConnectionString(name))
        {
            Username = name,
            Password = password,
        }.ConnectionString;
        return await BuildDatabaseAsync(connectionString, createSchema);
    }

    private static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
