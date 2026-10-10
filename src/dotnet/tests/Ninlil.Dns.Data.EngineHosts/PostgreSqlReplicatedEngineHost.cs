using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Ninlil.Dns.Common.Enum;
using Npgsql;

// Define our namespace.
namespace Ninlil.Dns.Data.EngineHosts;

/// <summary>
///     This class runs a PostgreSQL 16 primary and a hot standby that streams from it, on one
///     Docker network. The primary has <c>wal_level=replica</c> and a <c>repl</c> role that
///     <c>pg_hba.conf</c> admits for replication; the standby is seeded with
///     <c>pg_basebackup -R</c> and then starts as an ordinary server, so it refuses writes
///     ("cannot execute ... in a read-only transaction"). Replay is paused with
///     <c>pg_wal_replay_pause()</c>. Both are published on random host ports.
/// </summary>
public sealed class PostgreSqlReplicatedEngineHost : ReplicatedEngineHost
{
    private const string Password = "postgres";
    private const string ReplicationPassword = "repl-secret";

    private readonly INetwork _network = new NetworkBuilder().Build();
    private readonly IContainer _primary;
    private readonly IContainer _standby;

    /// <summary>
    ///     This constructor defines the two containers; <see cref="StartAsync" /> starts them.
    /// </summary>
    public PostgreSqlReplicatedEngineHost()
    {
        // The init script runs once on the primary's first start. "all" in pg_hba.conf never
        // matches a replication connection, so the entry is explicit.
        const string initScript =
            "#!/bin/sh\n"
            + "set -e\n"
            + $"psql -v ON_ERROR_STOP=1 -U postgres -c \"CREATE ROLE repl WITH REPLICATION LOGIN PASSWORD '{ReplicationPassword}'\"\n"
            + "echo 'host replication repl all scram-sha-256' >> \"$PGDATA/pg_hba.conf\"\n";

        _primary = new ContainerBuilder(PostgreSqlEngineHost.Image)
            .WithNetwork(_network)
            .WithNetworkAliases("pg-primary")
            .WithEnvironment("POSTGRES_PASSWORD", Password)
            .WithCommand(
                "postgres",
                "-c",
                "wal_level=replica",
                "-c",
                "max_wal_senders=5",
                "-c",
                "hot_standby=on"
            )
            .WithResourceMapping(
                System.Text.Encoding.UTF8.GetBytes(initScript),
                "/docker-entrypoint-initdb.d/10-replication.sh",
                // rwxr-xr-x
                0b111_101_101
            )
            .WithPortBinding(5432, assignRandomHostPort: true)
            .WithWaitStrategy(
                Wait.ForUnixContainer()
                    .UntilCommandIsCompleted("pg_isready", "-h", "127.0.0.1", "-U", "postgres")
            )
            .Build();

        // The standby copies the primary's data directory (retrying until the primary accepts
        // the replication login), then hands over to the image's normal entrypoint. The copy
        // runs as the postgres user so the files have the owner the server requires.
        const string standbyScript =
            "mkdir -p \"$PGDATA\" && chown postgres:postgres \"$PGDATA\" && chmod 700 \"$PGDATA\" && "
            + "until su-exec postgres pg_basebackup -h pg-primary -U repl -D \"$PGDATA\" -R -X stream; do sleep 1; done; "
            + "exec docker-entrypoint.sh postgres -c hot_standby=on";

        _standby = new ContainerBuilder(PostgreSqlEngineHost.Image)
            .WithNetwork(_network)
            .WithNetworkAliases("pg-standby")
            .WithEnvironment("PGPASSWORD", ReplicationPassword)
            .WithEnvironment("POSTGRES_PASSWORD", Password)
            .WithEntrypoint("sh", "-c")
            .WithCommand(standbyScript)
            .WithPortBinding(5432, assignRandomHostPort: true)
            .WithWaitStrategy(
                Wait.ForUnixContainer()
                    .UntilCommandIsCompleted("pg_isready", "-h", "127.0.0.1", "-U", "postgres")
            )
            .Build();
    }

    /// <inheritdoc />
    public override StorageEngineEnum Engine => StorageEngineEnum.PostgreSql;

    /// <inheritdoc />
    public override async Task StartAsync()
    {
        await _network.CreateAsync();
        await _primary.StartAsync();
        await _standby.StartAsync();

        // The standby is up once it accepts connections; it is streaming once it has replayed
        // a write, which the first catch-up proves.
        await WaitForCatchUpAsync();
    }

    /// <inheritdoc />
    public override async ValueTask DisposeAsync()
    {
        await _standby.DisposeAsync();
        await _primary.DisposeAsync();
        await _network.DisposeAsync();
    }

    /// <inheritdoc />
    public override string BuildConnectionString(string databaseName) =>
        Build(_primary, databaseName);

    /// <inheritdoc />
    public override string BuildReplicaConnectionString(string databaseName) =>
        Build(_standby, databaseName);

    /// <inheritdoc />
    public override async Task PauseReplicationAsync(string databaseName = null)
    {
        await ExecuteAsync(_standby, "SELECT pg_wal_replay_pause()");

        // The pause takes effect after the record being applied; wait until the server says so.
        await PollAsync(
            "the standby to pause replay",
            async () => (bool)await ScalarAsync(_standby, "SELECT pg_is_wal_replay_paused()")
        );
    }

    /// <inheritdoc />
    public override async Task ResumeReplicationAsync(string databaseName = null) =>
        await ExecuteAsync(_standby, "SELECT pg_wal_replay_resume()");

    /// <inheritdoc />
    public override async Task WaitForCatchUpAsync(
        string databaseName = null,
        TimeSpan? timeout = null
    )
    {
        // The standby has caught up once its replay position reaches the primary's current
        // write position. The comparison runs on the standby with the primary's LSN as text.
        string target = (string)await ScalarAsync(_primary, "SELECT pg_current_wal_lsn()::text");
        await PollAsync(
            "the standby to replay the primary's WAL",
            async () =>
                (bool)
                    await ScalarAsync(
                        _standby,
                        $"SELECT pg_last_wal_replay_lsn() >= '{target}'::pg_lsn"
                    ),
            timeout
        );
    }

    private static string Build(IContainer container, string databaseName, bool pooling = true) =>
        new NpgsqlConnectionStringBuilder
        {
            Host = container.Hostname,
            Port = container.GetMappedPublicPort(5432),
            Username = "postgres",
            Password = Password,
            Database = databaseName,
            Pooling = pooling,
        }.ConnectionString;

    private static async Task ExecuteAsync(IContainer container, string sql)
    {
        await using NpgsqlConnection connection = new(Build(container, "postgres", pooling: false));
        await connection.OpenAsync();
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object> ScalarAsync(IContainer container, string sql)
    {
        await using NpgsqlConnection connection = new(Build(container, "postgres", pooling: false));
        await connection.OpenAsync();
        await using NpgsqlCommand command = new(sql, connection);
        return await command.ExecuteScalarAsync();
    }
}
