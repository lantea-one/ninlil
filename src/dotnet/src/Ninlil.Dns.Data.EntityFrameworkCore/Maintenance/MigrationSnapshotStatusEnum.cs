// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Maintenance;

/// <summary>
///     This enum lists the states of a migration snapshot, as recorded in its
///     <see cref="MigrationSnapshotManifest" />. A snapshot starts as <see cref="Pending" />, ends
///     the attempt as <see cref="Succeeded" /> or <see cref="Failed" />, and later becomes
///     <see cref="Restored" /> (an operator restored it) or <see cref="Superseded" /> (a later
///     migration succeeded after it failed).
/// </summary>
public enum MigrationSnapshotStatusEnum
{
    /// <summary>
    ///     The snapshot was taken and the migration has not finished. A snapshot left in this
    ///     state means the process stopped while migrating.
    /// </summary>
    Pending,

    /// <summary>
    ///     The migration after this snapshot succeeded.
    /// </summary>
    Succeeded,

    /// <summary>
    ///     The migration after this snapshot failed and the database may be partly migrated. This
    ///     is the failed-migration marker: while it is the newest one for this build's last
    ///     migration, startup is refused.
    /// </summary>
    Failed,

    /// <summary>
    ///     An operator restored the database from this snapshot.
    /// </summary>
    Restored,

    /// <summary>
    ///     This snapshot failed or was left pending, and a later migration succeeded.
    /// </summary>
    Superseded,
}
