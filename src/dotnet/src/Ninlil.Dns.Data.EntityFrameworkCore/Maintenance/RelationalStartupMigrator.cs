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
    /// <summary>
    ///     This method checks the database against the migrations this build knows and applies the
    ///     pending ones.
    /// </summary>
    /// <param name="context">The context whose database is migrated.</param>
    /// <param name="storageOptions">The storage settings; the engine decides how a failure is read.</param>
    /// <param name="logger">The logger for progress messages.</param>
    /// <param name="stoppingToken">The token that cancels the work.</param>
    /// <exception cref="DatabaseStartupRefusedException">
    ///     The database holds migrations this build does not know, or applying migrations failed.
    /// </exception>
    public static async Task RunAsync(
        NinlilDnsContext context,
        StorageOptions storageOptions,
        ILogger logger,
        CancellationToken stoppingToken = default
    )
    {
        // A connection failure throws here, as it should: no marker is written and the process
        // exits non-zero so a supervisor retries once the database is up.
        List<string> applied = [.. await context.Database.GetAppliedMigrationsAsync(stoppingToken)];
        List<string> known = [.. context.Database.GetMigrations()];

        // A newer version applied migrations this build has never heard of. Its schema may differ
        // in ways this build's queries do not expect, so this build must not run on it.
        List<string> unknown = [.. applied.Except(known, StringComparer.Ordinal)];
        if (unknown.Count > 0)
            throw new DatabaseStartupRefusedException(
                "The database was upgraded by a newer Ninlil version (migrations it applied that "
                    + $"this version does not know: {string.Join(", ", unknown)}). Start that newer "
                    + "version, or restore a backup taken before the upgrade. This version will not "
                    + "start on it."
            );

        string failedMigration = await FindFailedMigrationAsync(context, stoppingToken);
        if (failedMigration != null)
            throw new DatabaseStartupRefusedException(failedMigration);

        List<string> pending = [.. known.Except(applied, StringComparer.Ordinal)];
        if (pending.Count == 0)
            return;

        // A fresh database has nothing worth a snapshot.
        string snapshot =
            applied.Count == 0 ? null : await TakeSnapshotAsync(context, stoppingToken);

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
                applied,
                pending,
                snapshot,
                exception,
                stoppingToken
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
        List<string> appliedBefore,
        List<string> pending,
        string snapshot,
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

        if (rolledBack)
        {
            await DiscardSnapshotAsync(snapshot, stoppingToken);
            return new DatabaseStartupRefusedException(
                $"Applying migrations {range} failed and was rolled back; the database is "
                    + $"unchanged. Cause: {cause.Message} Fix the cause and start again, or start "
                    + "the previous version.",
                cause
            );
        }

        await MarkSnapshotFailedAsync(snapshot, cause.Message, stoppingToken);
        string appliedText = appliedBefore.Count == 0 ? "none" : string.Join(", ", appliedBefore);
        return new DatabaseStartupRefusedException(
            $"Applying migrations {range} failed and was not rolled back; the database may be "
                + $"partly migrated. Migrations applied before the attempt: {appliedText}. Cause: "
                + $"{cause.Message} Restore a backup taken before the upgrade, or start the "
                + "previous version on a restored copy.",
            cause
        );
    }

    // Seam for the failed-migration marker (guard step 3). Returns the refusal text when an
    // earlier attempt on this database left a Failed marker for this build's last migration, or
    // null when there is none. No marker store exists yet, so it always answers null.
    private static Task<string> FindFailedMigrationAsync(
        NinlilDnsContext context,
        CancellationToken stoppingToken
    ) => Task.FromResult<string>(null);

    // Seam for the pre-migration snapshot (guard step 5). Returns the snapshot's id, or null when
    // none was taken. No snapshot store exists yet, so it always answers null.
    private static Task<string> TakeSnapshotAsync(
        NinlilDnsContext context,
        CancellationToken stoppingToken
    ) => Task.FromResult<string>(null);

    // Seam: drops a snapshot after a failure that was rolled back (it equals the database).
    private static Task DiscardSnapshotAsync(string snapshot, CancellationToken stoppingToken) =>
        Task.CompletedTask;

    // Seam: writes the Failed marker on a snapshot after a failure that was not rolled back.
    private static Task MarkSnapshotFailedAsync(
        string snapshot,
        string failure,
        CancellationToken stoppingToken
    ) => Task.CompletedTask;
}
