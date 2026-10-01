using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Server.Backup;
using Ninlil.Dns.Server.Backup.Model;
using Ninlil.Dns.Server.Backup.Options;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Services.Storage;

/// <summary>
///     This class implements <see cref="IStorageEngineSwitchService" />: it moves the database
///     onto another relational engine inside the running process, with no restart.
///     <para>
///         <b>How it works.</b> <c>AddNinlilDnsEntityFrameworkCore</c> registers one
///         <see cref="StorageOptions" /> singleton and closes over that instance in the
///         <c>AddDbContext&lt;NinlilDnsContext&gt;</c> configuration delegate. <c>AddDbContext</c>
///         registers <c>DbContextOptions</c> as scoped, so the delegate runs again for every new
///         scope, which in practice means every HTTP request. Changing
///         <see cref="StorageOptions.Engine" /> and <see cref="StorageOptions.ConnectionString" />
///         in place therefore changes what the next scope's <see cref="NinlilDnsContext" />, and
///         every repository built on it, connects to, with no container rebuild.
///     </para>
///     <para>
///         <b>Relational engines only.</b> The four relational engines share one EF Core model and
///         one set of repository classes; only the provider chosen by
///         <c>DbContextOptionsBuilderExtension.UseNinlilDnsStorage</c> differs.
///         <see cref="StorageEngineEnum.MongoDb" /> uses separate repository classes
///         (<c>Ninlil.Dns.Data.MongoDb</c>) chosen in <c>Program.cs</c> before the container is
///         built, so a switch to or from it is rejected.
///     </para>
///     <para>
///         <b>The sequence</b> in <see cref="SwitchEngineAsync" />: (1) migrate the target
///         database while the current engine keeps serving; (2) take a full backup of the current
///         engine with <see cref="IBackupService" />; (3) point the <see cref="StorageOptions" />
///         singleton at the target; (4) restore the backup into the target. A failure after step
///         3 points the singleton back at the previous engine, whose data is never changed; a
///         failure before it changes nothing. While a switch runs,
///         <see cref="StorageEngineSwitchState" /> makes the API answer 503.
///     </para>
///     <para>
///         <b>The change is not saved.</b> Only the in-memory singleton changes; the configuration
///         file is not updated (the restore in step 4 even writes back the archived copy), so after
///         a restart the process uses the previous engine again.
///     </para>
/// </summary>
/// <param name="currentOptions">
///     The <see cref="StorageOptions" /> singleton that <c>AddNinlilDnsEntityFrameworkCore</c>
///     registered and its <c>AddDbContext</c> delegate reads; changing it in place is how the
///     switch works.
/// </param>
/// <param name="backupService">Captures the data and restores it into the target.</param>
/// <param name="backupOptions">Read for the minimum passphrase length.</param>
/// <param name="state">The shared in-progress flag, claimed for the length of a switch.</param>
/// <param name="logger">Records a switch's progress and outcome.</param>
public sealed class StorageEngineSwitchService(
    StorageOptions currentOptions,
    IBackupService backupService,
    IOptions<BackupOptions> backupOptions,
    StorageEngineSwitchState state,
    ILogger<StorageEngineSwitchService> logger
) : IStorageEngineSwitchService
{
    /// <summary>
    ///     This field lists the engines <see cref="SwitchEngineAsync" /> can switch to or from
    ///     without a restart; the class summary explains why
    ///     <see cref="StorageEngineEnum.MongoDb" /> is not one of them.
    /// </summary>
    private static readonly IReadOnlyList<StorageEngineEnum> LiveSwitchSupportedEngines =
    [
        StorageEngineEnum.Sqlite,
        StorageEngineEnum.PostgreSql,
        StorageEngineEnum.MySql,
        StorageEngineEnum.SqlServer,
    ];

    /// <inheritdoc />
    public StorageStatus GetStatus() =>
        new(currentOptions.Engine, state.IsSwitchInProgress, LiveSwitchSupportedEngines);

    /// <inheritdoc />
    public async Task<StorageEngineSwitchResult> SwitchEngineAsync(
        StorageEngineSwitchRequest request,
        CancellationToken stoppingToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        if (
            currentOptions.Engine == StorageEngineEnum.MongoDb
            || request.TargetEngine == StorageEngineEnum.MongoDb
        )
            throw new ValidationException(
                "Live storage-engine switching supports SQLite, PostgreSQL, MySQL, and SQL Server "
                    + "only, since every one of those four is served by the same Entity Framework "
                    + "Core model and repository classes. MongoDB is served by an entirely separate "
                    + "repository implementation chosen once at process startup and cannot be "
                    + "switched into or out of this running process yet -- change the 'Storage' "
                    + "configuration section and restart instead."
            );

        if (string.IsNullOrWhiteSpace(request.TargetConnectionString))
            throw new ValidationException("A target connection string is required.");

        if (
            request.TargetEngine == currentOptions.Engine
            && string.Equals(
                request.TargetConnectionString,
                currentOptions.ConnectionString,
                StringComparison.Ordinal
            )
        )
            throw new ValidationException(
                "The target engine and connection string already match the currently active storage."
            );

        if (!state.TryBeginSwitch())
            throw new ConflictException("A storage-engine switch is already in progress.");

        StorageEngineEnum previousEngine = currentOptions.Engine;
        string previousConnectionString = currentOptions.ConnectionString;
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        bool flipped = false;

        try
        {
            logger.LogInformation(
                "Beginning a live storage-engine switch from {PreviousEngine} to {TargetEngine}.",
                previousEngine,
                request.TargetEngine
            );

            // Step 1: migrate the target while the current engine keeps serving, which proves the
            // target is reachable and ready before anything changes.
            StorageOptions targetProbe = new()
            {
                Engine = request.TargetEngine,
                ConnectionString = request.TargetConnectionString,
            };
            DbContextOptions<NinlilDnsContext> targetDbContextOptions =
                (DbContextOptions<NinlilDnsContext>)
                    new DbContextOptionsBuilder<NinlilDnsContext>()
                        .UseNinlilDnsStorage(targetProbe)
                        .Options;
            await using (NinlilDnsContext targetContext = new(targetDbContextOptions))
            {
                await targetContext.Database.MigrateAsync(stoppingToken);
            }

            // Step 2: back up the current engine. The passphrase is generated for this call, kept
            // only in memory and never returned or logged; the archive only carries data across
            // the switch.
            string transientPassphrase = GenerateTransientPassphrase();
            byte[] archive = await backupService.CreateBackupAsync(
                transientPassphrase,
                stoppingToken
            );

            // Step 3: flip. Every scope opened from now on, including the next request, gets a
            // NinlilDnsContext for the target (see the class summary).
            currentOptions.Engine = request.TargetEngine;
            currentOptions.ConnectionString = request.TargetConnectionString;
            flipped = true;

            // Step 4: restore the step 2 backup. The restore opens its scope after the flip, so its
            // repositories and unit of work use the target.
            BackupRestoreSummary summary = await backupService.RestoreBackupAsync(
                new MemoryStream(archive),
                transientPassphrase,
                stoppingToken
            );

            DateTimeOffset completedAt = DateTimeOffset.UtcNow;
            logger.LogInformation(
                "Switched the active storage engine from {PreviousEngine} to {NewEngine}, "
                    + "restoring {TotalEntities} rows across {EntityTypeCount} entity types, all "
                    + "within this same process ({DurationMilliseconds}ms).",
                previousEngine,
                request.TargetEngine,
                summary.TotalEntitiesImported,
                summary.EntityCounts.Count,
                (completedAt - startedAt).TotalMilliseconds
            );

            return new StorageEngineSwitchResult(
                previousEngine,
                request.TargetEngine,
                summary.TotalEntitiesImported,
                summary.EntityCounts,
                startedAt,
                completedAt
            );
        }
        catch (Exception exception)
            when (exception is not ValidationException and not ConflictException)
        {
            // Undo the flip, if it happened, so the previous engine, whose data no step changed,
            // stays in use. A ValidationException or ConflictException is not caught here and so
            // does not undo a flip.
            if (flipped)
            {
                currentOptions.Engine = previousEngine;
                currentOptions.ConnectionString = previousConnectionString;
            }

            logger.LogError(
                exception,
                "Storage-engine switch to {TargetEngine} failed; reverted to {PreviousEngine}.",
                request.TargetEngine,
                previousEngine
            );

            throw new ValidationException(
                $"The storage-engine switch failed and was rolled back to {previousEngine}: {exception.Message}"
            );
        }
        finally
        {
            state.EndSwitch();
        }
    }

    /// <summary>
    ///     This method generates a random passphrase comfortably longer than
    ///     <see cref="BackupOptions.MinimumPassphraseLength" /> for the in-memory archive
    ///     <see cref="SwitchEngineAsync" /> uses. It is never returned, logged or stored.
    /// </summary>
    private string GenerateTransientPassphrase()
    {
        int length = Math.Max(48, backupOptions.Value.MinimumPassphraseLength + 16);
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(length));
    }
}
