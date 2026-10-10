using MySqlConnector;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Replication;

/// <summary>
///     This class is the smoke test of the MySQL source and replica fixture, with real
///     containers: a row written on the source reaches the replica, does not reach it while the
///     SQL thread is stopped, appears after it is started, and the replica refuses writes even
///     from <c>root</c> (<c>super_read_only</c>). It needs Docker.
/// </summary>
[Collection(MySqlReplicatedCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MySqlReplicationTests(MySqlReplicatedEngineFixture fixture)
{
    private static async Task<long> CountAsync(string connectionString, string table)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using MySqlCommand command = new($"SELECT count(*) FROM {table}", connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using MySqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using MySqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<(string Primary, string Replica, string Table)> NewTableAsync()
    {
        string table = $"smoke_{Guid.NewGuid():N}";
        string primary = fixture.Replicated.BuildConnectionString("test");
        await ExecuteAsync(
            fixture.Replicated.BuildConnectionString("mysql"),
            "CREATE DATABASE IF NOT EXISTS test"
        );
        await ExecuteAsync(primary, $"CREATE TABLE {table} (id int)");
        return (primary, fixture.Replicated.BuildReplicaConnectionString("test"), table);
    }

    [Fact]
    public async Task Replica_AfterAWriteOnTheSource_ShowsTheRow()
    {
        (string primary, string replica, string table) = await NewTableAsync();
        await ExecuteAsync(primary, $"INSERT INTO {table} VALUES (1)");
        await fixture.Replicated.WaitForCatchUpAsync();

        Assert.Equal(1, await CountAsync(replica, table));
    }

    [Fact]
    public async Task Replica_WhileTheSqlThreadIsStopped_DoesNotShowTheRowUntilStarted()
    {
        (string primary, string replica, string table) = await NewTableAsync();
        await fixture.Replicated.WaitForCatchUpAsync();
        await fixture.Replicated.PauseReplicationAsync();
        try
        {
            await ExecuteAsync(primary, $"INSERT INTO {table} VALUES (1)");
            await Task.Delay(1000);
            Assert.Equal(0, await CountAsync(replica, table));
        }
        finally
        {
            await fixture.Replicated.ResumeReplicationAsync();
        }

        await fixture.Replicated.WaitForCatchUpAsync();
        Assert.Equal(1, await CountAsync(replica, table));
    }

    [Fact]
    public async Task Replica_OnAWrite_RefusesIt()
    {
        (_, string replica, string table) = await NewTableAsync();
        await fixture.Replicated.WaitForCatchUpAsync();

        MySqlException exception = await Assert.ThrowsAsync<MySqlException>(() =>
            ExecuteAsync(replica, $"INSERT INTO {table} VALUES (1)")
        );
        Assert.Equal(1290, exception.Number);
    }
}
