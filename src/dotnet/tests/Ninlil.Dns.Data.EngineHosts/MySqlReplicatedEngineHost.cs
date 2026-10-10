using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using MySqlConnector;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.EngineHosts;

/// <summary>
///     This class runs a MySQL 8.4 source and a GTID replica of it on one Docker network. The
///     source has the binary log and GTIDs on; the replica follows it with
///     <c>SOURCE_AUTO_POSITION=1</c> and then runs with <c>read_only</c> and
///     <c>super_read_only</c>, so even <c>root</c> cannot write to it (error 1290). Replication is
///     paused by stopping the SQL thread, which leaves the I/O thread fetching. Both connect as
///     <c>root</c> and are published on random host ports.
/// </summary>
public sealed class MySqlReplicatedEngineHost : ReplicatedEngineHost
{
    private const string RootPassword = "root";
    private const string ReplicationPassword = "repl-secret";

    private readonly INetwork _network = new NetworkBuilder().Build();
    private readonly IContainer _source;
    private readonly IContainer _replica;

    /// <summary>
    ///     This constructor defines the two containers; <see cref="StartAsync" /> starts them.
    /// </summary>
    public MySqlReplicatedEngineHost()
    {
        _source = Create("mysql-source", 1);
        _replica = Create("mysql-replica", 2);
    }

    /// <inheritdoc />
    public override StorageEngineEnum Engine => StorageEngineEnum.MySql;

    /// <inheritdoc />
    public override async Task StartAsync()
    {
        await _network.CreateAsync();
        await _source.StartAsync();
        await _replica.StartAsync();

        // The entrypoint of each image writes its own start-up statements (root account and so
        // on) to its binary log. The replica is told that the source's set is already applied,
        // so it does not re-run the source's "CREATE USER root" against its own root account.
        await ExecuteAsync(
            _source,
            $"CREATE USER 'repl'@'%' IDENTIFIED BY '{ReplicationPassword}'",
            "GRANT REPLICATION SLAVE ON *.* TO 'repl'@'%'"
        );
        string executed = (string)await ScalarAsync(_source, "SELECT @@GLOBAL.gtid_executed");
        await ExecuteAsync(
            _replica,
            "RESET BINARY LOGS AND GTIDS",
            $"SET GLOBAL gtid_purged = '{executed.Replace("\n", string.Empty)}'",
            "CHANGE REPLICATION SOURCE TO SOURCE_HOST = 'mysql-source', SOURCE_USER = 'repl', "
                + $"SOURCE_PASSWORD = '{ReplicationPassword}', SOURCE_AUTO_POSITION = 1, "
                + "GET_SOURCE_PUBLIC_KEY = 1",
            "START REPLICA",
            "SET GLOBAL super_read_only = ON",
            "SET GLOBAL read_only = ON"
        );

        await WaitForCatchUpAsync();
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        await _replica.DisposeAsync();
        await _source.DisposeAsync();
        await _network.DisposeAsync();
    }

    /// <inheritdoc />
    public override string BuildConnectionString(string databaseName) =>
        Build(_source, databaseName);

    /// <inheritdoc />
    public override string BuildReplicaConnectionString(string databaseName) =>
        Build(_replica, databaseName);

    /// <inheritdoc />
    public override Task PauseReplicationAsync(string databaseName = null) =>
        ExecuteAsync(_replica, "STOP REPLICA SQL_THREAD");

    /// <inheritdoc />
    public override Task ResumeReplicationAsync(string databaseName = null) =>
        ExecuteAsync(_replica, "START REPLICA SQL_THREAD");

    /// <inheritdoc />
    public override async Task WaitForCatchUpAsync(
        string databaseName = null,
        TimeSpan? timeout = null
    )
    {
        // WAIT_FOR_EXECUTED_GTID_SET returns 0 when the replica has applied the source's set,
        // and 1 on timeout.
        string target = (
            (string)await ScalarAsync(_source, "SELECT @@GLOBAL.gtid_executed")
        ).Replace("\n", string.Empty);
        int seconds = (int)(timeout ?? TimeSpan.FromSeconds(60)).TotalSeconds;
        object result = await ScalarAsync(
            _replica,
            $"SELECT WAIT_FOR_EXECUTED_GTID_SET('{target}', {seconds})"
        );
        if (Convert.ToInt32(result) != 0)
            throw new TimeoutException("Timed out waiting for the MySQL replica to catch up.");
    }

    private IContainer Create(string alias, int serverId) =>
        new ContainerBuilder(MySqlEngineHost.Image)
            .WithNetwork(_network)
            .WithNetworkAliases(alias)
            .WithEnvironment("MYSQL_ROOT_PASSWORD", RootPassword)
            .WithCommand(
                $"--server-id={serverId}",
                "--log-bin=mysql-bin",
                "--gtid-mode=ON",
                "--enforce-gtid-consistency=ON",
                "--log-replica-updates=ON"
            )
            .WithPortBinding(3306, assignRandomHostPort: true)
            .WithWaitStrategy(
                // The entrypoint's temporary server has no TCP listener, so a ping over TCP
                // succeeds only for the final server.
                Wait.ForUnixContainer()
                    .UntilCommandIsCompleted(
                        "mysqladmin",
                        "ping",
                        "-h",
                        "127.0.0.1",
                        "-uroot",
                        $"-p{RootPassword}",
                        "--silent"
                    )
            )
            .Build();

    private static string Build(IContainer container, string databaseName, bool pooling = true) =>
        new MySqlConnectionStringBuilder
        {
            Server = container.Hostname,
            Port = container.GetMappedPublicPort(3306),
            UserID = "root",
            Password = RootPassword,
            Database = databaseName,
            Pooling = pooling,
            AllowPublicKeyRetrieval = true,
        }.ConnectionString;

    private static async Task ExecuteAsync(IContainer container, params string[] statements)
    {
        await using MySqlConnection connection = new(Build(container, "mysql", pooling: false));
        await connection.OpenAsync();
        foreach (string sql in statements)
        {
            await using MySqlCommand command = new(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task<object> ScalarAsync(IContainer container, string sql)
    {
        await using MySqlConnection connection = new(Build(container, "mysql", pooling: false));
        await connection.OpenAsync();
        await using MySqlCommand command = new(sql, connection);
        return await command.ExecuteScalarAsync();
    }
}
