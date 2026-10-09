// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Maintenance;

/// <summary>
///     This class describes one migration snapshot: what was copied, when, by which build, and how
///     the migration that followed ended. An <see cref="IMigrationSnapshotStore" /> persists it
///     next to the snapshot data as JSON, and <c>RelationalStartupMigrator</c> reads it to find
///     the failed-migration marker, to prune old snapshots and to restore one.
/// </summary>
public class MigrationSnapshotManifest
{
    /// <summary>
    ///     This property contains the snapshot id, the UTC creation time as <c>yyyyMMddHHmmss</c>.
    ///     Ids sort in creation order.
    /// </summary>
    public string Id { get; set; }

    /// <summary>
    ///     This property contains the name of the storage engine (<c>StorageEngineEnum</c>) the
    ///     snapshot was taken from. A restore refuses a snapshot of another engine.
    /// </summary>
    public string Engine { get; set; }

    /// <summary>
    ///     This property contains the UTC time the snapshot was taken.
    /// </summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>
    ///     This property contains the informational version of the build that took the snapshot.
    /// </summary>
    public string ApplicationVersion { get; set; }

    /// <summary>
    ///     This property contains the migrations the database had applied when the snapshot was
    ///     taken.
    /// </summary>
    public List<string> AppliedMigrationsBefore { get; set; } = [];

    /// <summary>
    ///     This property contains the last migration of the build that took the snapshot, the one
    ///     the migration attempt aimed at.
    /// </summary>
    public string TargetMigration { get; set; }

    /// <summary>
    ///     This property contains every table copied, with its row count.
    /// </summary>
    public List<MigrationSnapshotTable> Tables { get; set; } = [];

    /// <summary>
    ///     This property contains the snapshot's state.
    /// </summary>
    public MigrationSnapshotStatusEnum Status { get; set; }

    /// <summary>
    ///     This property contains the UTC time <see cref="Status" /> last changed.
    /// </summary>
    public DateTime StatusUtc { get; set; }

    /// <summary>
    ///     This property contains the error message of the failed migration when
    ///     <see cref="Status" /> is <see cref="MigrationSnapshotStatusEnum.Failed" />.
    /// </summary>
    public string Failure { get; set; }

    /// <summary>
    ///     This property contains where the snapshot lives, in words an operator can act on (a
    ///     file path for SQLite). It never contains a connection string.
    /// </summary>
    public string Location { get; set; }
}

/// <summary>
///     This class records one table of a snapshot and how many rows it held, so a restore can
///     check that it gave back what was there.
/// </summary>
public class MigrationSnapshotTable
{
    /// <summary>
    ///     This property contains the table name.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     This property contains the number of rows the table held.
    /// </summary>
    public long Rows { get; set; }
}
