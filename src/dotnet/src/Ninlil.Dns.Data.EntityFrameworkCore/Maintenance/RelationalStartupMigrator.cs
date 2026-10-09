using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Exceptions;
using Ninlil.Dns.Data.Abstractions.Options;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Maintenance;

/// <summary>
///     This class applies the pending EF Core migrations at startup and guards the database while
///     it does. <c>Program</c> calls <see cref="RunAsync" /> once, on the relational engines, in
///     place of a bare <c>MigrateAsync</c>. It stops startup with a
///     <see cref="DatabaseStartupRefusedException" /> in two cases: the history table holds
///     migrations this build does not know (a newer Ninlil upgraded the database, and running
///     older code on it is unsafe), and a migration fails (the message says whether the database
///     was left unchanged). A connection failure is not a refusal; it throws as is, so a
///     supervisor retries while the database is still starting.
/// </summary>
public static class RelationalStartupMigrator
{
    private const string SettingsPrefix = "NINLIL_STORAGE_MIGRATION_SAFETY_";

    /// <summary>
    ///     This method restores the snapshot named in the settings (if any), checks the database
    ///     against the migrations this build knows, takes a snapshot when migrations are pending
    ///     on a database that holds data, and applies the pending migrations.
    /// </summary>
    /// <param name="context">The context whose database is migrated.</param>
    /// <param name="storageOptions">
    ///     The storage settings; the engine decides how a failure is read and how a snapshot is
    ///     taken, and <see cref="StorageOptions.MigrationSafety" /> holds the snapshot settings.
    /// </param>
    /// <param name="logger">The logger for progress messages.</param>
    /// <param name="stoppingToken">The token that cancels the work.</param>
    /// <exception cref="DatabaseStartupRefusedException">
    ///     The database holds migrations this build does not know, an earlier attempt left a
    ///     failed-migration marker, a snapshot or restore could not be done, or applying
    ///     migrations failed.
    /// </exception>
    public static async Task RunAsync(
        NinlilDnsContext context,
        StorageOptions storageOptions,
        ILogger logger,
        CancellationToken stoppingToken = default
    )
    {
        StorageMigrationSafetyOptions safety =
            storageOptions.MigrationSafety ?? new StorageMigrationSafetyOptions();
        IMigrationSnapshotStore store = MigrationSnapshotStores.Create(
            storageOptions.Engine,
            context
        );

        // A restore runs first so that nothing reads or changes the database before it.
        if (!string.IsNullOrWhiteSpace(safety.RestoreSnapshot))
            await RestoreAsync(
                store,
                storageOptions,
                safety.RestoreSnapshot.Trim(),
                logger,
                stoppingToken
            );

        // A connection failure throws here, as it should: no marker is written and the process
        // exits non-zero so a supervisor retries once the database is up.
        List<string> applied = [.. await context.Database.GetAppliedMigrationsAsync(stoppingToken)];
        List<string> known = [.. context.Database.GetMigrations()];

        // A newer version applied migrations this build has never heard of. Its schema may differ
        // in ways this build's queries do not expect, so this build must not run on it.
        List<string> unknown = [.. applied.Except(known, StringComparer.Ordinal)];
        if (unknown.Count > 0)
        {
            string restoreHint = "";
            MigrationSnapshotManifest newest =
                store == null ? null : (await store.ListAsync(stoppingToken)).LastOrDefault();
            if (newest != null)
                restoreHint = " " + store.DescribeRestore(newest);
            throw new DatabaseStartupRefusedException(
                "The database was upgraded by a newer Ninlil version (migrations it applied that "
                    + $"this version does not know: {string.Join(", ", unknown)}). Start that newer "
                    + "version, or restore a backup taken before the upgrade. This version will not "
                    + "start on it."
                    + restoreHint
            );
        }

        string target = known.Count == 0 ? null : known[^1];
        // A database with no migration history has no snapshots, and on a server engine it may
        // not exist yet, so its catalog cannot be read.
        MigrationSnapshotManifest failedMarker = await FindFailedMigrationAsync(
            applied.Count == 0 ? null : store,
            target,
            stoppingToken
        );
        if (failedMarker != null && !safety.RetryFailedMigration)
            throw new DatabaseStartupRefusedException(
                $"An earlier attempt to apply migration {target} failed on "
                    + $"{failedMarker.StatusUtc:u} and may have left the database partly migrated, "
                    + $"so this start is refused. The failure was: {failedMarker.Failure} "
                    + store.DescribeRestore(failedMarker)
                    + " To try the migration again on the database as it is, set "
                    + $"{SettingsPrefix}RETRY_FAILED_MIGRATION=true."
            );

        List<string> pending = [.. known.Except(applied, StringComparer.Ordinal)];
        if (pending.Count == 0)
            return;

        // A fresh database has nothing worth a snapshot.
        MigrationSnapshotManifest snapshot = null;
        bool reused = false;
        if (failedMarker != null)
        {
            snapshot = failedMarker;
            reused = true;
            logger.LogWarning(
                "Trying migration {Target} again after the failed attempt of {Time:u}; snapshot "
                    + "{Snapshot} of that attempt is kept.",
                target,
                failedMarker.StatusUtc,
                failedMarker.Id
            );
        }
        else if (applied.Count > 0)
        {
            snapshot = await TakeSnapshotAsync(
                store,
                safety,
                applied,
                target,
                logger,
                stoppingToken
            );
        }

        logger.LogInformation(
            "Applying {Count} pending database migration(s): {Migrations}.",
            pending.Count,
            string.Join(", ", pending)
        );

        try
        {
            await context.Database.MigrateAsync(stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw await DescribeFailureAsync(
                context,
                storageOptions,
                store,
                applied,
                pending,
                snapshot,
                reused,
                exception,
                stoppingToken
            );
        }

        if (snapshot != null)
            await FinishSnapshotsAsync(store, safety, snapshot, logger, stoppingToken);
    }

    /// <summary>
    ///     This method restores the snapshot with the given id, unless it was restored already.
    ///     A snapshot that is already restored is left alone with a warning, so a supervisor that
    ///     restarts the process with the setting still in place cannot restore twice over newer
    ///     data.
    /// </summary>
    private static async Task RestoreAsync(
        IMigrationSnapshotStore store,
        StorageOptions storageOptions,
        string id,
        ILogger logger,
        CancellationToken stoppingToken
    )
    {
        if (store == null)
            throw new DatabaseStartupRefusedException(
                $"{SettingsPrefix}RESTORE_SNAPSHOT is set to {id}, but snapshots are not available "
                    + $"for engine {storageOptions.Engine} with this connection. Remove the setting."
            );

        IReadOnlyList<MigrationSnapshotManifest> manifests = await store.ListAsync(stoppingToken);
        MigrationSnapshotManifest manifest = manifests.FirstOrDefault(candidate =>
            candidate.Id == id
        );
        if (manifest == null)
            throw new DatabaseStartupRefusedException(
                $"There is no snapshot {id} to restore. Snapshots of this database: "
                    + (
                        manifests.Count == 0
                            ? "none"
                            : string.Join(", ", manifests.Select(m => m.Id))
                    )
                    + $". Correct or remove {SettingsPrefix}RESTORE_SNAPSHOT."
            );

        if (manifest.Status == MigrationSnapshotStatusEnum.Restored)
        {
            logger.LogWarning(
                "Snapshot {Snapshot} was already restored at {Time:u}; it is not restored again. "
                    + "Remove the setting {Setting}.",
                id,
                manifest.StatusUtc,
                SettingsPrefix + "RESTORE_SNAPSHOT"
            );
            return;
        }

        if (manifest.Engine != storageOptions.Engine.ToString())
            throw new DatabaseStartupRefusedException(
                $"Snapshot {id} was taken from a {manifest.Engine} database and cannot be restored "
                    + $"into a {storageOptions.Engine} database."
            );

        logger.LogWarning("Restoring the database from snapshot {Snapshot}.", id);
        try
        {
            await store.RestoreAsync(manifest, stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new DatabaseStartupRefusedException(
                $"Restoring snapshot {id} failed: {exception.Message} The database may be partly "
                    + "restored; start again with the same setting to repeat the restore.",
                exception
            );
        }

        logger.LogWarning(
            "Snapshot {Snapshot} is restored. Remove the setting {Setting} before the next start.",
            id,
            SettingsPrefix + "RESTORE_SNAPSHOT"
        );
    }

    /// <summary>
    ///     This method returns the failed-migration marker: the newest snapshot of this database
    ///     with status Failed whose target is this build's last migration, or null.
    /// </summary>
    private static async Task<MigrationSnapshotManifest> FindFailedMigrationAsync(
        IMigrationSnapshotStore store,
        string target,
        CancellationToken stoppingToken
    )
    {
        if (store == null || target == null)
            return null;

        return (await store.ListAsync(stoppingToken)).LastOrDefault(manifest =>
            manifest.Status == MigrationSnapshotStatusEnum.Failed
            && manifest.TargetMigration == target
        );
    }

    /// <summary>
    ///     This method takes the snapshot before the migration, or returns null when the settings
    ///     or the engine say none is taken. A snapshot that cannot be taken stops the start
    ///     rather than migrating without a safety net.
    /// </summary>
    private static async Task<MigrationSnapshotManifest> TakeSnapshotAsync(
        IMigrationSnapshotStore store,
        StorageMigrationSafetyOptions safety,
        List<string> applied,
        string target,
        ILogger logger,
        CancellationToken stoppingToken
    )
    {
        if (!safety.TakeSnapshot)
        {
            logger.LogWarning(
                "{Setting} is false: the pending migrations are applied without a snapshot, so a "
                    + "failure cannot be undone from one.",
                SettingsPrefix + "TAKE_SNAPSHOT"
            );
            return null;
        }

        if (store == null)
        {
            logger.LogInformation(
                "No migration snapshot is taken: this storage engine or connection has no "
                    + "snapshot support."
            );
            return null;
        }

        Stopwatch watch = Stopwatch.StartNew();
        MigrationSnapshotManifest manifest;
        try
        {
            manifest = await store.CreateAsync(applied, target, stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new DatabaseStartupRefusedException(
                $"The snapshot before the migration could not be taken: {exception.Message} The "
                    + "database is unchanged. Fix the cause (disk space, permissions) and start "
                    + $"again, or set {SettingsPrefix}TAKE_SNAPSHOT=false to migrate without a "
                    + "snapshot.",
                exception
            );
        }

        logger.LogInformation(
            "Took migration snapshot {Snapshot} at {Location}: {Tables} tables, {Rows} rows, "
                + "{Milliseconds} ms.",
            manifest.Id,
            manifest.Location,
            manifest.Tables.Count,
            manifest.Tables.Sum(table => table.Rows),
            watch.ElapsedMilliseconds
        );
        return manifest;
    }

    /// <summary>
    ///     This method marks the snapshot Succeeded after a good migration and applies the
    ///     retention rule. Older Failed and Pending snapshots become Superseded; then only the
    ///     newest SnapshotsToKeep finished snapshots stay. A problem here is logged, never
    ///     thrown: the database is already migrated.
    /// </summary>
    private static async Task FinishSnapshotsAsync(
        IMigrationSnapshotStore store,
        StorageMigrationSafetyOptions safety,
        MigrationSnapshotManifest snapshot,
        ILogger logger,
        CancellationToken stoppingToken
    )
    {
        try
        {
            await store.SetStatusAsync(
                snapshot,
                MigrationSnapshotStatusEnum.Succeeded,
                null,
                stoppingToken
            );

            List<MigrationSnapshotManifest> all = [.. await store.ListAsync(stoppingToken)];
            foreach (
                MigrationSnapshotManifest older in all.Where(manifest =>
                    manifest.Status
                        is MigrationSnapshotStatusEnum.Failed
                            or MigrationSnapshotStatusEnum.Pending
                )
            )
                await store.SetStatusAsync(
                    older,
                    MigrationSnapshotStatusEnum.Superseded,
                    older.Failure,
                    stoppingToken
                );

            int keep = Math.Max(1, safety.SnapshotsToKeep);
            List<MigrationSnapshotManifest> finished =
            [
                .. all.Where(manifest =>
                    manifest.Status
                        is MigrationSnapshotStatusEnum.Succeeded
                            or MigrationSnapshotStatusEnum.Restored
                            or MigrationSnapshotStatusEnum.Superseded
                ),
            ];
            foreach (
                MigrationSnapshotManifest old in finished.Take(Math.Max(0, finished.Count - keep))
            )
            {
                await store.DeleteAsync(old, stoppingToken);
                logger.LogInformation("Deleted old migration snapshot {Snapshot}.", old.Id);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "The migration succeeded, but the snapshot housekeeping failed."
            );
        }
    }

    /// <summary>
    ///     This method reads the state after a failed <c>MigrateAsync</c> and builds the refusal.
    ///     The failure counts as rolled back when the engine's DDL is transactional and the
    ///     applied list is unchanged. SQLite, PostgreSQL and SQL Server run the batch in one
    ///     transaction. MySQL commits every DDL statement on its own, so a failed batch can leave
    ///     a half-applied schema even when the history shows nothing new; it is never reported as
    ///     rolled back.
    /// </summary>
    private static async Task<DatabaseStartupRefusedException> DescribeFailureAsync(
        NinlilDnsContext context,
        StorageOptions storageOptions,
        IMigrationSnapshotStore store,
        List<string> appliedBefore,
        List<string> pending,
        MigrationSnapshotManifest snapshot,
        bool snapshotReused,
        Exception cause,
        CancellationToken stoppingToken
    )
    {
        bool rolledBack = false;
        if (storageOptions.Engine != StorageEngineEnum.MySql)
        {
            try
            {
                context.ChangeTracker.Clear();
                List<string> appliedNow =
                [
                    .. await context.Database.GetAppliedMigrationsAsync(stoppingToken),
                ];
                rolledBack = appliedNow.SequenceEqual(appliedBefore, StringComparer.Ordinal);
            }
            catch (Exception)
            {
                // The state cannot be read, so it cannot be promised unchanged.
            }
        }

        string range = pending.Count == 1 ? pending[0] : $"{pending[0]}..{pending[^1]}";

        if (rolledBack && !snapshotReused)
        {
            // The snapshot equals the database, so it is of no use.
            if (snapshot != null)
                await store.DeleteAsync(snapshot, stoppingToken);
            return new DatabaseStartupRefusedException(
                $"Applying migrations {range} failed and was rolled back; the database is "
                    + $"unchanged. Cause: {cause.Message} Fix the cause and start again, or start "
                    + "the previous version.",
                cause
            );
        }

        string appliedText = appliedBefore.Count == 0 ? "none" : string.Join(", ", appliedBefore);
        if (snapshot == null)
            return new DatabaseStartupRefusedException(
                $"Applying migrations {range} failed and was not rolled back; the database may be "
                    + $"partly migrated. Migrations applied before the attempt: {appliedText}. Cause: "
                    + $"{cause.Message} No snapshot was taken. Restore a backup taken before the "
                    + "upgrade, or start the previous version on a restored copy.",
                cause
            );

        await store.SetStatusAsync(
            snapshot,
            MigrationSnapshotStatusEnum.Failed,
            cause.Message,
            stoppingToken
        );
        return new DatabaseStartupRefusedException(
            $"Applying migrations {range} failed"
                + (
                    rolledBack
                        ? " and was rolled back"
                        : " and was not rolled back; the database may be partly migrated"
                )
                + $". Migrations applied before the attempt: {appliedText}. Cause: {cause.Message} "
                + store.DescribeRestore(snapshot)
                + " Until it is restored this version refuses to start; to try again on the "
                + $"database as it is, set {SettingsPrefix}RETRY_FAILED_MIGRATION=true.",
            cause
        );
    }
}
