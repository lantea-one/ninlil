using System.Data.Common;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Routing;
using Npgsql;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Topology;

/// <summary>
///     This class measures the read replica of PostgreSQL, MySQL and SQL Server for the
///     <see cref="ReplicaHealthMonitor" />. It opens its own short-lived ADO.NET connections (not
///     the application's contexts) to the read string and, for the schema comparison, to the
///     primary string.
///     <list type="bullet">
///         <item>
///             Lag, PostgreSQL: seconds since the last replayed transaction while the standby has
///             received WAL it has not replayed, otherwise 0 (an idle primary makes the replay
///             timestamp age, which is not lag). A behind reading is confirmed after a short wait
///             so a standby that is merely applying a fresh write is not reported as lagging.
///         </item>
///         <item>
///             Lag, MySQL: <c>Seconds_Behind_Source</c> of <c>SHOW REPLICA STATUS</c>. NULL means
///             replication is stopped, which is unknown. A server with no replica status is not a
///             replica and has lag 0.
///         </item>
///         <item>
///             Lag, SQL Server: the availability group's view of the secondary, read on the
///             primary (the secondary does not see the primary's commit times): the reported
///             <c>secondary_lag_seconds</c> while the secondary synchronises, and for a suspended
///             secondary the time between its last redone log record and the primary's last
///             commit. It needs the <c>VIEW SERVER STATE</c> permission; without it the lag is
///             unknown and the detail says so.
///         </item>
///         <item>
///             Schema: the newest <c>MigrationId</c> of <c>__EFMigrationsHistory</c> on the replica
///             equals the primary's. A database created without migrations has no history table on
///             either side, which counts as equal.
///         </item>
///     </list>
///     With a multi-host read string the check measures whichever host the driver picks for that
///     connection; it is one sample per interval.
/// </summary>
/// <param name="engine">The relational engine.</param>
/// <param name="primaryConnectionString">The composed primary connection string.</param>
/// <param name="replicaConnectionString">The composed read connection string.</param>
public sealed class RelationalReplicaHealthProbe(
    StorageEngineEnum engine,
    string primaryConnectionString,
    string replicaConnectionString
) : IReplicaHealthProbe
{
    private const int CommandTimeoutSeconds = 15;

    /// <inheritdoc />
    public async Task<ReplicaProbeResult> ProbeAsync(CancellationToken stoppingToken)
    {
        await using DbConnection replica = Create(replicaConnectionString);
        await replica.OpenAsync(stoppingToken);

        double? lag;
        string detail = "";
        switch (engine)
        {
            case StorageEngineEnum.PostgreSql:
                lag = await PostgreSqlLagAsync(replica, stoppingToken);
                break;
            case StorageEngineEnum.MySql:
                (lag, detail) = await MySqlLagAsync(replica, stoppingToken);
                break;
            case StorageEngineEnum.SqlServer:
                (lag, detail) = await SqlServerLagAsync(stoppingToken);
                break;
            default:
                throw new NotSupportedException(
                    $"The {engine} engine has no relational replica probe."
                );
        }

        string replicaNewest = await NewestMigrationAsync(replica, stoppingToken);
        string primaryNewest;
        await using (DbConnection primary = Create(primaryConnectionString))
        {
            await primary.OpenAsync(stoppingToken);
            primaryNewest = await NewestMigrationAsync(primary, stoppingToken);
        }

        bool schemaCurrent = string.Equals(replicaNewest, primaryNewest, StringComparison.Ordinal);
        if (!schemaCurrent)
            detail =
                "The replica is on schema version "
                + $"{replicaNewest ?? "(none)"} but the primary is on {primaryNewest ?? "(none)"}.";

        return new ReplicaProbeResult(lag, schemaCurrent, detail);
    }

    private DbConnection Create(string connectionString) =>
        engine switch
        {
            StorageEngineEnum.PostgreSql => new NpgsqlConnection(connectionString),
            StorageEngineEnum.MySql => new MySqlConnection(connectionString),
            StorageEngineEnum.SqlServer => new SqlConnection(connectionString),
            _ => throw new NotSupportedException($"The {engine} engine has no relational probe."),
        };

    private static async Task<object> ScalarAsync(
        DbConnection connection,
        string sql,
        CancellationToken stoppingToken
    )
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = CommandTimeoutSeconds;
        object value = await command.ExecuteScalarAsync(stoppingToken);
        return value is DBNull ? null : value;
    }

    /// <summary>
    ///     This query is 0 on a server that is not in recovery or has replayed everything it
    ///     received, and otherwise the age of the last replayed transaction (null when nothing
    ///     was replayed yet, which is unknown).
    /// </summary>
    private const string PostgreSqlLagSql =
        "SELECT CASE WHEN NOT pg_is_in_recovery() THEN 0::float8 "
        + "WHEN pg_last_wal_receive_lsn() IS NOT DISTINCT FROM pg_last_wal_replay_lsn() THEN 0::float8 "
        + "ELSE EXTRACT(EPOCH FROM now() - pg_last_xact_replay_timestamp())::float8 END";

    private static async Task<double?> PostgreSqlLagAsync(
        DbConnection replica,
        CancellationToken stoppingToken
    )
    {
        double? lag = ToDouble(await ScalarAsync(replica, PostgreSqlLagSql, stoppingToken));
        if (lag is null or 0)
            return lag;

        // The standby may be applying a write that just arrived. Look again before calling it
        // behind.
        await Task.Delay(TimeSpan.FromMilliseconds(300), stoppingToken);
        return ToDouble(await ScalarAsync(replica, PostgreSqlLagSql, stoppingToken));
    }

    private static async Task<(double? Lag, string Detail)> MySqlLagAsync(
        DbConnection replica,
        CancellationToken stoppingToken
    )
    {
        await using DbCommand command = replica.CreateCommand();
        command.CommandText = "SHOW REPLICA STATUS";
        command.CommandTimeout = CommandTimeoutSeconds;
        await using DbDataReader reader = await command.ExecuteReaderAsync(stoppingToken);

        // No row means the server is not a replica (a read string that names the primary), which
        // has nothing to catch up on. With several channels the worst one counts.
        double? worst = 0;
        string detail = "";
        int behind = reader.GetOrdinal("Seconds_Behind_Source");
        while (await reader.ReadAsync(stoppingToken))
        {
            if (await reader.IsDBNullAsync(behind, stoppingToken))
            {
                worst = null;
                detail = "MySQL replication is stopped, so the lag is unknown.";
                break;
            }

            worst = Math.Max(worst.Value, Convert.ToDouble(reader.GetValue(behind)));
        }

        return (worst, detail);
    }

    private async Task<(double? Lag, string Detail)> SqlServerLagAsync(
        CancellationToken stoppingToken
    )
    {
        await using DbConnection primary = Create(primaryConnectionString);
        await primary.OpenAsync(stoppingToken);

        // One row per replica of this database: the local one (the primary) and one per
        // secondary. The primary sees its secondaries' state; a secondary does not see the
        // primary's commit times.
        await using DbCommand command = primary.CreateCommand();
        command.CommandText = SqlServerLagSql;
        command.CommandTimeout = CommandTimeoutSeconds;
        List<SqlServerReplicaRow> rows = [];
        try
        {
            await using DbDataReader reader = await command.ExecuteReaderAsync(stoppingToken);
            while (await reader.ReadAsync(stoppingToken))
                rows.Add(
                    new SqlServerReplicaRow(
                        reader.GetBoolean(0),
                        !reader.IsDBNull(1) && reader.GetBoolean(1),
                        reader.IsDBNull(2) ? "" : reader.GetString(2),
                        reader.IsDBNull(3) ? 0 : Convert.ToInt64(reader.GetValue(3)),
                        reader.IsDBNull(4) ? 0 : Convert.ToInt64(reader.GetValue(4)),
                        reader.IsDBNull(5) ? null : Convert.ToDouble(reader.GetValue(5)),
                        reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                        reader.IsDBNull(7) ? null : reader.GetDateTime(7)
                    )
                );
        }
        catch (SqlException exception) when (exception.Number is 297 or 300 or 229)
        {
            return (null, "Reading the replica lag needs VIEW SERVER STATE on the primary.");
        }

        return SqlServerLag(rows);
    }

    /// <summary>
    ///     This record is one row of <c>sys.dm_hadr_database_replica_states</c>.
    /// </summary>
    private sealed record SqlServerReplicaRow(
        bool IsLocal,
        bool IsSuspended,
        string SynchronizationState,
        long LogSendQueueKb,
        long RedoQueueKb,
        double? LagSeconds,
        DateTime? LastCommitTime,
        DateTime? LastRedoneTime
    );

    /// <summary>
    ///     This method turns the rows into the worst lag of any secondary. No rows at all means
    ///     the database is not in an availability group, which has nothing to lag behind. A
    ///     suspended or not synchronising secondary does not report a lag, so it is measured as the
    ///     time between its last redone log record and the primary's last commit (0 when the
    ///     primary has committed nothing newer). A group with no secondary row is unknown.
    /// </summary>
    private static (double? Lag, string Detail) SqlServerLag(List<SqlServerReplicaRow> rows)
    {
        if (rows.Count == 0)
            return (0, "");

        SqlServerReplicaRow local = rows.FirstOrDefault(row => row.IsLocal);
        List<SqlServerReplicaRow> secondaries = [.. rows.Where(row => !row.IsLocal)];
        if (secondaries.Count == 0)
            return (null, "SQL Server reports no secondary for the database.");

        double worst = 0;
        foreach (SqlServerReplicaRow secondary in secondaries)
        {
            bool stalled =
                secondary.IsSuspended
                || !(
                    secondary.SynchronizationState.Equals(
                        "SYNCHRONIZING",
                        StringComparison.OrdinalIgnoreCase
                    )
                    || secondary.SynchronizationState.Equals(
                        "SYNCHRONIZED",
                        StringComparison.OrdinalIgnoreCase
                    )
                );

            if (!stalled && secondary.LagSeconds is not null)
            {
                worst = Math.Max(worst, secondary.LagSeconds.Value);
                continue;
            }

            if (!stalled && secondary.LogSendQueueKb == 0 && secondary.RedoQueueKb == 0)
                continue;

            if (secondary.LastRedoneTime is null)
                return (null, "SQL Server has not reported progress of the secondary yet.");

            if (local?.LastCommitTime is not null)
                worst = Math.Max(
                    worst,
                    (local.LastCommitTime.Value - secondary.LastRedoneTime.Value).TotalSeconds
                );
        }

        return (worst, "");
    }

    private const string SqlServerLagSql =
        "SELECT is_local, is_suspended, synchronization_state_desc, log_send_queue_size, "
        + "redo_queue_size, secondary_lag_seconds, last_commit_time, last_redone_time "
        + "FROM sys.dm_hadr_database_replica_states WHERE database_id = DB_ID()";

    private async Task<string> NewestMigrationAsync(
        DbConnection connection,
        CancellationToken stoppingToken
    )
    {
        string exists = engine switch
        {
            StorageEngineEnum.PostgreSql =>
                "SELECT CASE WHEN to_regclass('\"__EFMigrationsHistory\"') IS NULL THEN 0 ELSE 1 END",
            StorageEngineEnum.MySql => "SELECT COUNT(*) FROM information_schema.tables "
                + "WHERE table_schema = DATABASE() AND table_name = '__EFMigrationsHistory'",
            _ =>
                "SELECT CASE WHEN OBJECT_ID(N'__EFMigrationsHistory', N'U') IS NULL THEN 0 ELSE 1 END",
        };

        if (Convert.ToInt64(await ScalarAsync(connection, exists, stoppingToken)) == 0)
            return null;

        // MigrationId starts with a sortable timestamp, so the highest string is the newest.
        string newest = engine switch
        {
            StorageEngineEnum.PostgreSql =>
                "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\" DESC LIMIT 1",
            StorageEngineEnum.MySql =>
                "SELECT `MigrationId` FROM `__EFMigrationsHistory` ORDER BY `MigrationId` DESC LIMIT 1",
            _ =>
                "SELECT TOP 1 [MigrationId] FROM [__EFMigrationsHistory] ORDER BY [MigrationId] DESC",
        };
        return (string)await ScalarAsync(connection, newest, stoppingToken);
    }

    private static double? ToDouble(object value) =>
        value is null
            ? null
            : Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
}
