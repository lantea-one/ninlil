// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Options;

/// <summary>
///     This class binds to the <c>Storage:MigrationSafety</c> section (nested as
///     <see cref="StorageOptions.MigrationSafety" />, so it has no section constant of its own)
///     and controls what Ninlil does to protect a relational database while it applies pending
///     migrations at startup. Before it applies them to a database that already holds data, <c>RelationalStartupMigrator</c> takes a snapshot
///     of that database (SQLite: a file next to the database). The settings below say whether it
///     does, how many it keeps, how it treats an earlier failed attempt, and which snapshot to
///     restore.
/// </summary>
public class StorageMigrationSafetyOptions
{
    /// <summary>
    ///     This property says whether a snapshot is taken before pending migrations are applied
    ///     to a database that already holds data. When it is false the migrations run without a
    ///     snapshot and a warning is logged. A fresh database never gets a snapshot.
    /// </summary>
    public bool TakeSnapshot { get; set; } = true;

    /// <summary>
    ///     This property contains how many finished snapshots are kept after a successful
    ///     migration; the oldest are deleted first. Values below 1 are treated as 1.
    /// </summary>
    public int SnapshotsToKeep { get; set; } = 3;

    /// <summary>
    ///     This property says whether a start may try again after an earlier migration attempt
    ///     failed and left its database possibly half migrated. When it is false (the default)
    ///     the start is refused until the snapshot of that attempt is restored. When it is true
    ///     the start reuses that snapshot and migrates again.
    /// </summary>
    public bool RetryFailedMigration { get; set; }

    /// <summary>
    ///     This property contains the id of a snapshot to restore at the next start, before
    ///     anything else touches the database. It is empty when no restore is wanted. The setting
    ///     must be removed after the restore; a snapshot that was already restored is not
    ///     restored a second time.
    /// </summary>
    public string RestoreSnapshot { get; set; } = "";
}
