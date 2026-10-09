using Microsoft.Data.SqlClient;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Options;
using Testcontainers.MsSql;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Fixtures;

/// <summary>
///     This class runs one SQL Server 2022 container for the SQL Server collection, pinned by
///     digest. The image is large (about 1.5 GB compressed) and exists for amd64 only, so the
///     first run on a machine pays a long pull.
/// </summary>
public sealed class SqlServerEngineFixture : EngineFixtureBase
{
    private readonly MsSqlContainer _container = new MsSqlBuilder(
        "mcr.microsoft.com/mssql/server:2022-latest@sha256:4402d880dd4c34bfa7d8705e56a86cd6c88da80a1f6bbbe741f999e76264a090"
    )
        .WithPortBinding(MsSqlBuilder.MsSqlPort, assignRandomHostPort: true)
        .Build();

    /// <inheritdoc />
    public override StorageEngineEnum Engine => StorageEngineEnum.SqlServer;

    /// <inheritdoc />
    public override Task InitializeAsync() => _container.StartAsync();

    /// <inheritdoc />
    public override Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <inheritdoc />
    protected override string BuildConnectionString(string databaseName) =>
        new SqlConnectionStringBuilder(_container.GetConnectionString())
        {
            InitialCatalog = databaseName,
        }.ConnectionString;

    /// <inheritdoc />
    public override async Task<EngineDatabase> CreateLeastPrivilegeDatabaseAsync(
        bool createSchema = false
    )
    {
        // The statements follow src/script/database/sqlserver/init.sql: a login that is
        // db_owner in its own database only.
        string name = $"ninlil_{Guid.NewGuid():N}";
        string password = $"Pw-{Guid.NewGuid():N}-1!";
        await using (SqlConnection admin = new(BuildConnectionString("master")))
        {
            await admin.OpenAsync();
            await Execute(admin, $"CREATE DATABASE [{name}]");
            await Execute(
                admin,
                $"CREATE LOGIN [{name}] WITH PASSWORD = N'{password}', CHECK_POLICY = ON"
            );
        }

        await using (SqlConnection inDatabase = new(BuildConnectionString(name)))
        {
            await inDatabase.OpenAsync();
            await Execute(inDatabase, $"CREATE USER [{name}] FOR LOGIN [{name}]");
            await Execute(inDatabase, $"ALTER ROLE [db_owner] ADD MEMBER [{name}]");
        }

        string connectionString = new SqlConnectionStringBuilder(BuildConnectionString(name))
        {
            UserID = name,
            Password = password,
        }.ConnectionString;
        return await BuildDatabaseAsync(connectionString, createSchema);
    }

    private static async Task Execute(SqlConnection connection, string sql)
    {
        await using SqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
