using MySqlConnector;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Maintenance;

/// <summary>
///     This static class asks a MySQL server whether it is read-only. MySqlConnector cannot tell a
///     writable server from a read-only one, so a multi-host primary string (fail-over mode) or a
///     failed-over cluster can leave Ninlil connected to a replica. A replica sets the global
///     <c>read_only</c> variable, and every write on it fails. <see cref="RelationalStartupMigrator" />
///     calls <see cref="IsReadOnlyAsync" /> before it migrates and refuses to start on a read-only
///     server, and the <c>storage</c> health check calls it to report Unhealthy. Callers ask only
///     when the engine is MySQL.
/// </summary>
public static class MySqlPrimaryCheck
{
    /// <summary>
    ///     This method reports whether the server behind the connection string has
    ///     <c>@@global.read_only</c> set. It connects without the database name, because the
    ///     database may not exist yet on a first start and the variable is global.
    /// </summary>
    /// <param name="connectionString">The primary MySQL connection string; it is not changed.</param>
    /// <param name="stoppingToken">The token that cancels the work.</param>
    /// <returns>True when the server refuses writes by configuration.</returns>
    public static async Task<bool> IsReadOnlyAsync(
        string connectionString,
        CancellationToken stoppingToken = default
    )
    {
        MySqlConnectionStringBuilder builder = new(connectionString) { Database = "" };

        await using MySqlConnection connection = new(builder.ConnectionString);
        await connection.OpenAsync(stoppingToken);
        await using MySqlCommand command = new("SELECT @@global.read_only", connection);
        object value = await command.ExecuteScalarAsync(stoppingToken);
        return value is not null && Convert.ToInt64(value) != 0;
    }
}
