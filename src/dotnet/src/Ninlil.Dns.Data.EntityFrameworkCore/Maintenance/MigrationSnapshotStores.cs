using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Maintenance;

/// <summary>
///     This static class picks the <see cref="IMigrationSnapshotStore" /> for the configured
///     engine. It is the one place that maps an engine to a store.
/// </summary>
public static class MigrationSnapshotStores
{
    /// <summary>
    ///     This method returns the store for the engine of <paramref name="context" />, or null
    ///     when the engine or the database has no snapshot support (an in-memory SQLite database,
    ///     or MongoDB, which has its own safety net). The caller then migrates without a
    ///     snapshot.
    /// </summary>
    /// <param name="engine">The configured storage engine.</param>
    /// <param name="context">The context whose database is snapshotted.</param>
    public static IMigrationSnapshotStore Create(
        StorageEngineEnum engine,
        NinlilDnsContext context
    ) =>
        engine switch
        {
            StorageEngineEnum.Sqlite => SqliteMigrationSnapshotStore.TryCreate(
                context.Database.GetConnectionString()
            ),
            StorageEngineEnum.PostgreSql => new PostgreSqlMigrationSnapshotStore(context),
            StorageEngineEnum.MySql => new MySqlMigrationSnapshotStore(context),
            StorageEngineEnum.SqlServer => new SqlServerMigrationSnapshotStore(context),
            _ => null,
        };
}
