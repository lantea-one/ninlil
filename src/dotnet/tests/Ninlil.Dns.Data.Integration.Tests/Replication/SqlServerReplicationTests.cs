using Microsoft.Data.SqlClient;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Replication;

/// <summary>
///     This class is the smoke test of the SQL Server read-scale availability group fixture,
///     with real containers: a row written on the primary reaches the secondary through the
///     <c>ApplicationIntent=ReadOnly</c> connection, does not reach it while data movement is
///     suspended, appears after resume, the secondary rejects a connection without the intent,
///     and its database refuses writes. It needs Docker.
/// </summary>
[Collection(SqlServerReplicatedCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class SqlServerReplicationTests(SqlServerReplicatedEngineFixture fixture)
{
    private SqlServerReplicatedEngineHost Host => (SqlServerReplicatedEngineHost)fixture.Replicated;

    private static async Task<int> CountAsync(string connectionString)
    {
        await using SqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using SqlCommand command = new("SELECT count(*) FROM smoke", connection);
        return (int)await command.ExecuteScalarAsync();
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using SqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using SqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<string> NewDatabaseAsync()
    {
        string name = $"smoke_{Guid.NewGuid():N}";
        await Host.CreateReplicatedDatabaseAsync(name);
        await ExecuteAsync(Host.BuildConnectionString(name), "CREATE TABLE smoke (id int)");
        await Host.WaitForCatchUpAsync(name);

        return name;
    }

    [Fact]
    public async Task Secondary_AfterAWriteOnThePrimary_ShowsTheRow()
    {
        string name = await NewDatabaseAsync();
        await ExecuteAsync(Host.BuildConnectionString(name), "INSERT INTO smoke VALUES (1)");
        await Host.WaitForCatchUpAsync(name);

        Assert.Equal(1, await CountAsync(Host.BuildReplicaConnectionString(name)));
    }

    [Fact]
    public async Task Secondary_WhileDataMovementIsSuspended_DoesNotShowTheRowUntilResumed()
    {
        string name = await NewDatabaseAsync();
        await Host.PauseReplicationAsync(name);
        try
        {
            await ExecuteAsync(Host.BuildConnectionString(name), "INSERT INTO smoke VALUES (1)");
            await Task.Delay(1000);
            Assert.Equal(0, await CountAsync(Host.BuildReplicaConnectionString(name)));
        }
        finally
        {
            await Host.ResumeReplicationAsync(name);
        }

        await Host.WaitForCatchUpAsync(name);
        Assert.Equal(1, await CountAsync(Host.BuildReplicaConnectionString(name)));
    }

    [Fact]
    public async Task Secondary_WithoutTheReadOnlyIntent_RejectsTheConnection()
    {
        string name = await NewDatabaseAsync();

        await Assert.ThrowsAsync<SqlException>(() =>
            CountAsync(Host.BuildReplicaConnectionStringWithoutIntent(name))
        );
    }

    [Fact]
    public async Task Secondary_OnAWrite_RefusesIt()
    {
        string name = await NewDatabaseAsync();

        SqlException exception = await Assert.ThrowsAsync<SqlException>(() =>
            ExecuteAsync(Host.BuildReplicaConnectionString(name), "INSERT INTO smoke VALUES (1)")
        );
        // 3906: the database is read-only.
        Assert.Equal(3906, exception.Number);
    }
}
