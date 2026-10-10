using Ninlil.Dns.Data.Integration.Tests.Fixtures;
using Npgsql;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Replication;

/// <summary>
///     This class is the smoke test of the PostgreSQL primary and standby fixture, with real
///     containers: a row written on the primary reaches the standby, does not reach it while
///     replay is paused, appears after resume, and the standby refuses writes. It needs Docker.
/// </summary>
[Collection(PostgreSqlReplicatedCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class PostgreSqlReplicationTests(PostgreSqlReplicatedEngineFixture fixture)
{
    private static async Task<long> CountAsync(string connectionString, string table)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new($"SELECT count(*) FROM {table}", connection);
        return (long)await command.ExecuteScalarAsync();
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<(string Primary, string Replica, string Table)> NewTableAsync()
    {
        string table = $"smoke_{Guid.NewGuid():N}";
        string primary = fixture.Replicated.BuildConnectionString("postgres");
        await ExecuteAsync(primary, $"CREATE TABLE {table} (id int)");
        return (primary, fixture.Replicated.BuildReplicaConnectionString("postgres"), table);
    }

    [Fact]
    public async Task Replica_AfterAWriteOnThePrimary_ShowsTheRow()
    {
        (string primary, string replica, string table) = await NewTableAsync();
        await ExecuteAsync(primary, $"INSERT INTO {table} VALUES (1)");
        await fixture.Replicated.WaitForCatchUpAsync();

        Assert.Equal(1, await CountAsync(replica, table));
    }

    [Fact]
    public async Task Replica_WhileReplayIsPaused_DoesNotShowTheRowUntilResumed()
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

        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync(replica, $"INSERT INTO {table} VALUES (1)")
        );
        Assert.Equal("25006", exception.SqlState);
    }
}
