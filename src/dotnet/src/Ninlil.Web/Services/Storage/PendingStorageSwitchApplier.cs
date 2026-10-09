using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Exceptions;
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Data.EntityFrameworkCore.Maintenance;
using Ninlil.Dns.Server.Backup;
using Ninlil.Dns.Server.Backup.Model;
using Ninlil.Dns.Server.Backup.Options;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Models;
using Ninlil.Web.Options;

// Define our namespace.
namespace Ninlil.Web.Services.Storage;

/// <summary>
///     This class applies a saved storage switch while the process starts. <c>Program.cs</c>
///     calls <see cref="ApplyAsync" /> once, after the startup migrator has migrated the
///     configured engine and before anything else touches the database. No background service
///     runs yet, so nothing writes while the data is copied, and the source holds every row
///     written up to the previous shutdown.
///     <para>
///         <b>Mechanism.</b> <c>AddNinlilDnsEntityFrameworkCore</c> registers one
///         <see cref="StorageOptions" /> singleton and closes over that instance in the
///         <c>AddDbContext</c> configuration delegate, which runs again for every scope. Changing
///         <see cref="StorageOptions.Engine" /> and <see cref="StorageOptions.ConnectionString" />
///         in place therefore changes what every later scope connects to. The copy is a backup of
///         the source restored into the target through <see cref="IBackupService" />, with
///         <c>restoreFiles: false</c> so the key ring, certificate and configuration file stay.
///     </para>
///     <para>
///         <b>Cases.</b> (1) The configured storage is the record's source: the target is
///         migrated, the data is copied, and, when the runtime configuration file decides the
///         storage settings, the file is rewritten to the target. If another source decides, the
///         record stays and a warning says so. (2) The configured storage is the record's target
///         (the operator changed the environment variable): the singleton is pointed at the
///         source for the export, then back. (3) Neither: a warning, and the record stays.
///     </para>
///     <para>
///         <b>Failure.</b> If the source is still the configured storage, the singleton is put
///         back on it, the record is deleted and the failure is recorded. If the target is the
///         configured storage, the start is refused with
///         <see cref="DatabaseStartupRefusedException" /> and the record stays, because the
///         process cannot run on a target that was not filled.
///     </para>
/// </summary>
/// <param name="store">Keeps the saved switch.</param>
/// <param name="storageOptions">The singleton the database contexts read.</param>
/// <param name="scopeFactory">Opens scopes for the backup service.</param>
/// <param name="configuration">The configuration root, searched for the origin of the storage settings.</param>
/// <param name="runtimeConfigurationOptions">Names the runtime configuration file the switch rewrites.</param>
/// <param name="backupOptions">Read for the minimum passphrase length.</param>
/// <param name="outcomeRecorder">Receives the outcome for the status API.</param>
/// <param name="logger">Records progress and failures.</param>
public sealed class PendingStorageSwitchApplier(
    PendingStorageSwitchStore store,
    StorageOptions storageOptions,
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    RuntimeConfigurationOptions runtimeConfigurationOptions,
    IOptions<BackupOptions> backupOptions,
    StorageSwitchOutcomeRecorder outcomeRecorder,
    ILogger<PendingStorageSwitchApplier> logger
)
{
    /// <summary>
    ///     This method applies the saved switch, if there is one and the configuration allows it.
    /// </summary>
    /// <param name="stoppingToken">The token that cancels the work.</param>
    /// <exception cref="DatabaseStartupRefusedException">
    ///     The target is the configured storage and could not be filled from the source.
    /// </exception>
    public async Task ApplyAsync(CancellationToken stoppingToken = default)
    {
        PendingStorageSwitch pending = store.Load();
        if (pending is null)
            return;

        bool configuredIsSource = Matches(pending.SourceEngine, pending.SourceConnectionString);
        bool configuredIsTarget = Matches(pending.TargetEngine, pending.TargetConnectionString);

        if (!configuredIsSource && !configuredIsTarget)
        {
            logger.LogWarning(
                "A storage switch from {Source} to {Target} is pending, but the configured storage "
                    + "is {Configured}, which is neither. The switch is kept; cancel it with "
                    + "DELETE /api/storage/switch or point the configuration at one of them.",
                pending.SourceEngine,
                pending.TargetEngine,
                storageOptions.Engine
            );
            return;
        }

        if (configuredIsSource)
        {
            StorageConfigurationOriginResult origin = StorageConfigurationOrigin.Resolve(
                (IConfigurationRoot)configuration,
                runtimeConfigurationOptions.ConfigurationFilePath
            );
            if (!origin.IsRuntimeFile)
            {
                logger.LogWarning(
                    "A storage switch to {Target} is pending, but {Origin} now decides the "
                        + "storage settings, so Ninlil does not change them. Point the storage "
                        + "settings at the new database and restart, or cancel the switch with "
                        + "DELETE /api/storage/switch.",
                    pending.TargetEngine,
                    origin.SourceName
                );
                return;
            }
        }

        await CopyAsync(pending, configuredIsSource, stoppingToken);
    }

    /// <summary>
    ///     This method copies the data from the source to the target and finishes the switch. The
    ///     class summary describes the two cases and the failure rules.
    /// </summary>
    private async Task CopyAsync(
        PendingStorageSwitch pending,
        bool configuredIsSource,
        CancellationToken stoppingToken
    )
    {
        // The singleton holds the configured storage until the flip to the target below.
        StorageEngineEnum configuredEngine = storageOptions.Engine;
        string configuredConnectionString = storageOptions.ConnectionString;
        int rowsCopied = 0;

        try
        {
            logger.LogInformation(
                "Applying the saved storage switch from {Source} to {Target}.",
                pending.SourceEngine,
                pending.TargetEngine
            );

            // The database that is not configured has not been through the startup migrator.
            if (configuredIsSource)
                await MigrateAsync(
                    pending.TargetEngine,
                    pending.TargetConnectionString,
                    stoppingToken
                );
            else
                await MigrateAsync(
                    pending.SourceEngine,
                    pending.SourceConnectionString,
                    stoppingToken
                );

            // Export from the source: it is the configured storage in the first case and only
            // pointed at for the export in the second.
            Point(pending.SourceEngine, pending.SourceConnectionString);
            string passphrase = GenerateTransientPassphrase();
            byte[] archive;
            await using (AsyncServiceScope exportScope = scopeFactory.CreateAsyncScope())
            {
                archive = await exportScope
                    .ServiceProvider.GetRequiredService<IBackupService>()
                    .CreateBackupAsync(passphrase, stoppingToken);
            }

            // Restore into the target. The restore opens its scope after the flip, so its
            // repositories use the target.
            Point(pending.TargetEngine, pending.TargetConnectionString);
            BackupRestoreSummary summary;
            await using (AsyncServiceScope restoreScope = scopeFactory.CreateAsyncScope())
            {
                summary = await restoreScope
                    .ServiceProvider.GetRequiredService<IBackupService>()
                    .RestoreBackupAsync(
                        new MemoryStream(archive),
                        passphrase,
                        stoppingToken,
                        restoreFiles: false
                    );
            }
            rowsCopied = summary.TotalEntitiesImported;

            if (configuredIsSource)
                await PresetConfigurationFileApplier.ApplyAsync(
                    [
                        new PresetConfigurationSetting(
                            StorageOptions.SectionName,
                            nameof(StorageOptions.Engine),
                            pending.TargetEngine.ToString()
                        ),
                        new PresetConfigurationSetting(
                            StorageOptions.SectionName,
                            nameof(StorageOptions.ConnectionString),
                            pending.TargetConnectionString
                        ),
                    ],
                    runtimeConfigurationOptions.ConfigurationFilePath
                );

            store.Delete();
            outcomeRecorder.Record(
                new StorageSwitchOutcome(
                    true,
                    pending.SourceEngine,
                    pending.TargetEngine,
                    DateTimeOffset.UtcNow,
                    rowsCopied,
                    $"Copied {rowsCopied} rows from {pending.SourceEngine} to {pending.TargetEngine}."
                )
            );
            logger.LogInformation(
                "Switched the storage from {Source} to {Target}, copying {Rows} rows.",
                pending.SourceEngine,
                pending.TargetEngine,
                rowsCopied
            );
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Whatever the failure, the process goes back to the storage it was configured with.
            storageOptions.Engine = configuredEngine;
            storageOptions.ConnectionString = configuredConnectionString;

            string message =
                $"The storage switch from {pending.SourceEngine} to {pending.TargetEngine} failed: "
                + exception.Message;
            outcomeRecorder.Record(
                new StorageSwitchOutcome(
                    false,
                    pending.SourceEngine,
                    pending.TargetEngine,
                    DateTimeOffset.UtcNow,
                    0,
                    message
                )
            );
            logger.LogError(exception, "{Message}", message);

            if (configuredIsSource)
            {
                // The source still serves, so the operator can request the switch again.
                store.Delete();
                return;
            }

            throw new DatabaseStartupRefusedException(
                $"The configured target database ({pending.TargetEngine}) could not be filled "
                    + $"from {pending.SourceEngine}: {exception.Message} Point the configuration "
                    + "back at the source or fix the cause.",
                exception
            );
        }
        catch (OperationCanceledException)
        {
            storageOptions.Engine = configuredEngine;
            storageOptions.ConnectionString = configuredConnectionString;
            throw;
        }
    }

    /// <summary>
    ///     This method reports whether the singleton currently holds this engine and connection
    ///     string.
    /// </summary>
    private bool Matches(StorageEngineEnum engine, string connectionString) =>
        storageOptions.Engine == engine
        && string.Equals(
            storageOptions.ConnectionString,
            connectionString,
            StringComparison.Ordinal
        );

    /// <summary>
    ///     This method points every later database scope at this engine and connection string.
    /// </summary>
    private void Point(StorageEngineEnum engine, string connectionString)
    {
        storageOptions.Engine = engine;
        storageOptions.ConnectionString = connectionString;
    }

    /// <summary>
    ///     This method runs the startup migrator on a database that is not the configured one, so
    ///     it gets the same guards: it is created or migrated, a database that a newer Ninlil
    ///     upgraded is refused, and a database that holds data is snapshotted first. A restore
    ///     or retry setting is never carried over, because those settings name the configured
    ///     database.
    /// </summary>
    private async Task MigrateAsync(
        StorageEngineEnum engine,
        string connectionString,
        CancellationToken stoppingToken
    )
    {
        StorageMigrationSafetyOptions configured =
            storageOptions.MigrationSafety ?? new StorageMigrationSafetyOptions();
        StorageOptions probe = new()
        {
            Engine = engine,
            ConnectionString = connectionString,
            MigrationSafety = new StorageMigrationSafetyOptions
            {
                TakeSnapshot = configured.TakeSnapshot,
                SnapshotsToKeep = configured.SnapshotsToKeep,
            },
        };

        DbContextOptions<NinlilDnsContext> options =
            (DbContextOptions<NinlilDnsContext>)
                new DbContextOptionsBuilder<NinlilDnsContext>().UseNinlilDnsStorage(probe).Options;
        await using NinlilDnsContext context = new(options);

        await RelationalStartupMigrator.RunAsync(context, probe, logger, stoppingToken);
    }

    /// <summary>
    ///     This method generates a random passphrase comfortably longer than
    ///     <see cref="BackupOptions.MinimumPassphraseLength" /> for the in-memory archive. It is
    ///     never returned, logged or stored.
    /// </summary>
    private string GenerateTransientPassphrase()
    {
        int length = Math.Max(48, backupOptions.Value.MinimumPassphraseLength + 16);
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(length));
    }
}
