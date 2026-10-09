using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Sqlite;

/// <summary>
///     This class runs <c>PRAGMA synchronous = NORMAL</c> on every SQLite connection EF Core
///     opens. <c>synchronous</c> is a per-connection setting, so it cannot be stored in the
///     database file. With the write-ahead log that
///     <see cref="Maintenance.RelationalStartupMigrator" /> enables, NORMAL keeps the database
///     consistent after any crash and skips the fsync on every commit; a power cut can lose the
///     last commits. <c>DbContextOptionsBuilderExtension.UseNinlilDnsSqlite</c> adds the shared
///     <see cref="Instance" /> to the context options.
/// </summary>
public sealed class SqliteConnectionPragmaInterceptor : DbConnectionInterceptor
{
    private const string Pragma = "PRAGMA synchronous = NORMAL;";

    /// <summary>
    ///     This property contains the shared instance; the interceptor holds no state.
    /// </summary>
    public static SqliteConnectionPragmaInterceptor Instance { get; } = new();

    /// <inheritdoc />
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using DbCommand command = connection.CreateCommand();
        command.CommandText = Pragma;
        command.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken stoppingToken = default
    )
    {
        await using DbCommand command = connection.CreateCommand();
        command.CommandText = Pragma;
        await command.ExecuteNonQueryAsync(stoppingToken);
    }
}
