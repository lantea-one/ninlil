// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Maintenance;

/// <summary>
///     This interface is the engine-specific half of the migration safety net. A store copies the
///     database aside before pending migrations run, keeps a <see cref="MigrationSnapshotManifest" />
///     for each copy, and puts a copy back on request. <c>RelationalStartupMigrator</c> holds all
///     the engine-independent policy (when to snapshot, the failed-migration marker, retention,
///     messages) and calls the store for the rest. <see cref="SqliteMigrationSnapshotStore" />
///     implements it for SQLite; the server engines add their own and are chosen in
///     <see cref="MigrationSnapshotStores.Create" />.
/// </summary>
public interface IMigrationSnapshotStore
{
    /// <summary>
    ///     This method copies the database, writes a manifest with status
    ///     <see cref="MigrationSnapshotStatusEnum.Pending" /> and returns it. The store picks the
    ///     id from the current UTC time and moves on to the next second when that id exists. On
    ///     failure it removes what it created and throws.
    /// </summary>
    /// <param name="appliedMigrationsBefore">The migrations the database has applied now.</param>
    /// <param name="targetMigration">The last migration of this build.</param>
    /// <param name="stoppingToken">The token that cancels the work.</param>
    Task<MigrationSnapshotManifest> CreateAsync(
        IReadOnlyList<string> appliedMigrationsBefore,
        string targetMigration,
        CancellationToken stoppingToken
    );

    /// <summary>
    ///     This method sets the status (and failure text) on <paramref name="manifest" /> and
    ///     saves it.
    /// </summary>
    Task SetStatusAsync(
        MigrationSnapshotManifest manifest,
        MigrationSnapshotStatusEnum status,
        string failure,
        CancellationToken stoppingToken
    );

    /// <summary>
    ///     This method returns the manifests of every snapshot of this database, oldest first.
    /// </summary>
    Task<IReadOnlyList<MigrationSnapshotManifest>> ListAsync(CancellationToken stoppingToken);

    /// <summary>
    ///     This method deletes a snapshot and its manifest.
    /// </summary>
    Task DeleteAsync(MigrationSnapshotManifest manifest, CancellationToken stoppingToken);

    /// <summary>
    ///     This method replaces the database content with the snapshot and sets its status to
    ///     <see cref="MigrationSnapshotStatusEnum.Restored" />. It throws when the restored
    ///     content does not match the manifest.
    /// </summary>
    Task RestoreAsync(MigrationSnapshotManifest manifest, CancellationToken stoppingToken);

    /// <summary>
    ///     This method returns the operator instructions for restoring
    ///     <paramref name="manifest" />, naming the snapshot and every restore method.
    /// </summary>
    string DescribeRestore(MigrationSnapshotManifest manifest);
}
