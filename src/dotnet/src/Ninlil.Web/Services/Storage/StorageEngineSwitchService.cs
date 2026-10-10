using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Options;
using Ninlil.Dns.Data.Abstractions.Routing;
using Ninlil.Dns.Data.Abstractions.Topology;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Data.MongoDb.Topology;
using Ninlil.Web.Models;
using Ninlil.Web.Options;

// Define our namespace.
namespace Ninlil.Web.Services.Storage;

/// <summary>
///     This class implements <see cref="IStorageEngineSwitchService" />: the request side of a
///     storage switch. It opens the target to prove it is reachable and not ahead of this build,
///     saves a pending switch with <see cref="PendingStorageSwitchStore" />, and tells the caller
///     whether the next start applies it by itself (the runtime configuration file decides the
///     storage settings) or the operator must first change an environment variable or another
///     source (see <see cref="StorageConfigurationOrigin" />). The copy itself runs in
///     <see cref="PendingStorageSwitchApplier" /> before anything else uses the database, so no
///     background service can write while the data moves.
///     <para>
///         Only the four relational engines can take part; MongoDB uses separate repositories
///         chosen at startup and is refused.
///     </para>
/// </summary>
/// <param name="currentOptions">
///     The <see cref="StorageOptions" /> singleton that holds the engine in use.
/// </param>
/// <param name="connectionStrings">
///     The connection strings composed at startup, read for the host names of the primary and
///     the replica in <see cref="GetStatus" />.
/// </param>
/// <param name="router">Supplies the replica health and the routed-read counters for <see cref="GetStatus" />.</param>
/// <param name="store">Keeps the pending switch.</param>
/// <param name="outcomeRecorder">Supplies the outcome of this start's switch to the status.</param>
/// <param name="configuration">The configuration root, searched for the origin of the storage settings.</param>
/// <param name="runtimeConfigurationOptions">Names the runtime configuration file.</param>
/// <param name="logger">Records a request.</param>
public sealed class StorageEngineSwitchService(
    StorageOptions currentOptions,
    StorageConnectionStrings connectionStrings,
    IStorageReadRouter router,
    PendingStorageSwitchStore store,
    StorageSwitchOutcomeRecorder outcomeRecorder,
    IConfiguration configuration,
    RuntimeConfigurationOptions runtimeConfigurationOptions,
    ILogger<StorageEngineSwitchService> logger
) : IStorageEngineSwitchService
{
    /// <summary>
    ///     This field is how long the target gets to answer the check before the request fails.
    /// </summary>
    private static readonly TimeSpan TargetCheckTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    ///     This field lists the engines a switch can target; the class summary explains why
    ///     <see cref="StorageEngineEnum.MongoDb" /> is not one of them.
    /// </summary>
    private static readonly IReadOnlyList<StorageEngineEnum> SwitchSupportedEngines =
    [
        StorageEngineEnum.Sqlite,
        StorageEngineEnum.PostgreSql,
        StorageEngineEnum.MySql,
        StorageEngineEnum.SqlServer,
    ];

    /// <inheritdoc />
    public StorageStatus GetStatus()
    {
        PendingStorageSwitch pending = store.Peek();
        PendingStorageSwitchInfo info = pending is null
            ? null
            : new PendingStorageSwitchInfo(
                pending.TargetEngine,
                pending.RequestedAtUtc,
                pending.RequestedBy,
                ResolveOrigin().IsRuntimeFile
            );

        return new StorageStatus(
            currentOptions.Engine,
            info,
            outcomeRecorder.Current,
            SwitchSupportedEngines,
            new StoragePrimaryStatus(
                StorageHostLister.List(currentOptions.Engine, PrimaryConnectionString())
            ),
            DescribeReadReplica()
        );
    }

    /// <summary>
    ///     This method returns the connection string the primary uses now. The composed string
    ///     is only valid while it was composed from the engine and string in use: a storage
    ///     switch applied at startup changes both in place, and the switch is refused while a
    ///     replica is configured, so the operator's string is then the primary.
    /// </summary>
    private string PrimaryConnectionString() =>
        connectionStrings.IsComposedFrom(currentOptions)
            ? connectionStrings.Primary
            : currentOptions.ConnectionString;

    /// <summary>
    ///     This method builds the replica part of the status, or returns null when no split is
    ///     configured.
    /// </summary>
    private StorageReadReplicaStatus DescribeReadReplica()
    {
        if (!router.SplitConfigured)
            return null;

        ReplicaHealthStatus health = router.ReplicaHealth;
        bool mongo = currentOptions.Engine == StorageEngineEnum.MongoDb;
        string replicaString = connectionStrings.IsComposedFrom(currentOptions)
            ? connectionStrings.Replica
            : null;

        return new StorageReadReplicaStatus(
            true,
            health.IsEligible,
            health.LagSeconds,
            health.SchemaCurrent,
            health.CheckedAtUtc,
            health.Reason,
            router.ReadsToReplica,
            router.ReadsToPrimary,
            mongo ? [] : StorageHostLister.List(currentOptions.Engine, replicaString),
            mongo
                ? MongoClientSettingsComposer
                    .ComposeReplicaReadPreference(currentOptions)
                    ?.ReadPreferenceMode.ToString()
                : null
        );
    }

    /// <inheritdoc />
    public async Task<StorageSwitchScheduled> ScheduleSwitchAsync(
        StorageEngineSwitchRequest request,
        string requestedBy,
        CancellationToken stoppingToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        if (
            currentOptions.Engine == StorageEngineEnum.MongoDb
            || request.TargetEngine == StorageEngineEnum.MongoDb
        )
            throw new ValidationException(
                "Storage switching supports SQLite, PostgreSQL, MySQL, and SQL Server only, since "
                    + "every one of those four is served by the same Entity Framework Core model "
                    + "and repository classes. MongoDB is served by an entirely separate "
                    + "repository implementation chosen once at process startup and cannot be "
                    + "switched into or out of yet."
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

        EnsureNoReadReplicaSettings();

        bool targetHasData = await CheckTargetAsync(request, stoppingToken);

        DateTimeOffset requestedAt = DateTimeOffset.UtcNow;
        await store.SaveAsync(
            new PendingStorageSwitch(
                currentOptions.Engine,
                currentOptions.ConnectionString,
                request.TargetEngine,
                request.TargetConnectionString,
                requestedAt,
                requestedBy
            ),
            stoppingToken
        );

        StorageConfigurationOriginResult origin = ResolveOrigin();
        string note = origin.IsRuntimeFile
            ? "Restart Ninlil to switch. The data is copied at that start."
            : $"Set NINLIL_STORAGE_ENGINE={request.TargetEngine} and "
                + "NINLIL_STORAGE_CONNECTION_STRING to the new database (or change "
                + $"{origin.SourceName}), then restart. The data is copied at that start.";

        logger.LogInformation(
            "{RequestedBy} scheduled a storage switch from {Source} to {Target} for the next start.",
            requestedBy,
            currentOptions.Engine,
            request.TargetEngine
        );

        return new StorageSwitchScheduled(
            request.TargetEngine,
            requestedAt,
            origin.IsRuntimeFile,
            targetHasData,
            note
        );
    }

    /// <inheritdoc />
    public bool CancelSwitch() => store.Delete();

    /// <summary>
    ///     This method refuses a switch while a read replica or a cluster setting of the engine in
    ///     use is configured: those settings belong to one engine, and may come from environment
    ///     variables Ninlil cannot change, so the operator removes them before switching. The
    ///     message names the settings, never a connection string.
    /// </summary>
    /// <exception cref="ValidationException">A read replica or cluster setting is configured.</exception>
    private void EnsureNoReadReplicaSettings()
    {
        if (!StorageTopologyValidator.HasClusterSettings(currentOptions))
            return;

        throw new ValidationException(
            "A storage switch is refused while read replica or cluster settings are configured "
                + $"({StorageOptions.SectionName}:ReadReplica and the "
                + $"{StorageOptions.SectionName}:{currentOptions.Engine} section). They belong to "
                + $"{currentOptions.Engine}. Remove the read-replica settings, switch, then "
                + "configure replicas for the new engine."
        );
    }

    /// <summary>
    ///     This method finds the origin of the storage settings in the running configuration.
    /// </summary>
    private StorageConfigurationOriginResult ResolveOrigin() =>
        StorageConfigurationOrigin.Resolve(
            (IConfigurationRoot)configuration,
            runtimeConfigurationOptions.ConfigurationFilePath
        );

    /// <summary>
    ///     This method opens the target and reads its applied migrations within
    ///     <see cref="TargetCheckTimeout" />. It rejects an unreachable target and one that a
    ///     newer Ninlil has upgraded. The exception message never includes the connection string.
    /// </summary>
    /// <returns>True when the target already holds Ninlil migrations, so its data is replaced.</returns>
    private static async Task<bool> CheckTargetAsync(
        StorageEngineSwitchRequest request,
        CancellationToken stoppingToken
    )
    {
        StorageOptions targetProbe = new()
        {
            Engine = request.TargetEngine,
            ConnectionString = request.TargetConnectionString,
        };

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken
        );
        timeout.CancelAfter(TargetCheckTimeout);

        try
        {
            DbContextOptions<NinlilDnsContext> options =
                (DbContextOptions<NinlilDnsContext>)
                    new DbContextOptionsBuilder<NinlilDnsContext>()
                        .UseNinlilDnsStorage(targetProbe)
                        .Options;
            await using NinlilDnsContext context = new(options);

            List<string> applied =
            [
                .. await context.Database.GetAppliedMigrationsAsync(timeout.Token),
            ];
            List<string> unknown =
            [
                .. applied.Except(context.Database.GetMigrations(), StringComparer.Ordinal),
            ];
            if (unknown.Count > 0)
                throw new ValidationException(
                    "The target database was upgraded by a newer Ninlil version (migrations this "
                        + $"version does not know: {string.Join(", ", unknown)}). Use a database "
                        + "this version can read."
                );

            return applied.Count > 0;
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            throw new ValidationException(
                $"The target database did not answer within {TargetCheckTimeout.TotalSeconds:0} seconds."
            );
        }
        catch (Exception exception)
            when (exception is not ValidationException and not OperationCanceledException)
        {
            throw new ValidationException(
                $"The target database could not be reached: {exception.Message}"
            );
        }
    }
}
