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
    ///     or an engine whose store does not exist yet). The caller then migrates without a
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
            _ => null,
        };
}
