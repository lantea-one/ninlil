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
///     This class implements <see cref="IStorageEngineSwitchService" />, the mechanism proving
///     this host's storage-dependent layer can genuinely be reconfigured onto a different
///     database engine within the same running process -- no Docker, no systemd, no process
///     exit/respawn.
///     <para>
///         <b>The mechanism itself.</b> <c>Ninlil.Dns.Data.EntityFrameworkCore</c>'s own
///         <c>AddNinlilDnsEntityFrameworkCore</c> registers exactly one
///         <see cref="StorageOptions" /> instance as a singleton, and closes over that <em>same</em>
///         instance inside the delegate passed to <c>AddDbContext&lt;NinlilDnsContext&gt;</c>.
///         Entity Framework Core's own <c>AddDbContext</c> registers <c>DbContextOptions</c>
///         with a <em>scoped</em> lifetime by default, which means that delegate is re-invoked
///         fresh every time a new dependency-injection scope resolves <see cref="NinlilDnsContext" />
///         for the first time -- in practice, once per incoming HTTP request, since ASP.NET Core
///         opens one scope per request automatically. Mutating the singleton's own
///         <see cref="StorageOptions.Engine" />/<see cref="StorageOptions.ConnectionString" />
///         properties in place therefore changes what the <em>next</em> scope's own
///         <see cref="NinlilDnsContext" /> connects to, with no dependency-injection
///         re-registration, no new <see cref="IServiceProvider" />, and no restart of any kind --
///         confirmed empirically against this exact application's own composition (a fresh
///         <see cref="NinlilDnsContext" /> resolved in a new scope, opened after the mutation,
///         genuinely reads and writes the new engine, repeatably, all within one unchanged
///         operating-system process) before this class was written around it. Every repository
///         this project defines is itself resolved fresh, per scope, from
///         <see cref="NinlilDnsContext" /> the same way -- so the same mutation reaches every
///         one of them with no separate handling per repository interface.
///     </para>
///     <para>
///         <b>Why this covers only the four relational engines.</b> The mechanism above only
///         changes which relational connection <see cref="NinlilDnsContext" /> resolves
///         against -- every one of the four relational engines is served by the exact same
///         Entity Framework Core model and the exact same concrete repository classes, with only
///         the underlying ADO.NET provider (<c>UseSqlite</c>/<c>UseNpgsql</c>/<c>UseMySql</c>/
///         <c>UseSqlServer</c>) differing, itself selected fresh, per scope, by
///         <c>DbContextOptionsBuilderExtension.UseNinlilDnsStorage</c> reading the very same
///         <see cref="StorageOptions.Engine" /> this class mutates.
///         <see cref="StorageEngineEnum.MongoDb" /> is served by an entirely separate concrete
///         repository implementation (<c>Ninlil.Dns.Data.MongoDb</c>), chosen once, by
///         <c>Program.cs</c>, before the dependency-injection container is even built --
///         switching to or from it would require every repository interface to be resolved
///         through a further indirection layer capable of pointing at a different concrete
///         implementation type at runtime, not merely a different connection through the same
///         type. That is real, additional engineering this class deliberately does not attempt, so
///         this class
///         rejects a request naming <see cref="StorageEngineEnum.MongoDb" /> on either side with a
///         clear, actionable message rather than attempting something unproven.
///     </para>
///     <para>
///         <b>The switch sequence itself</b>, run by <see cref="SwitchEngineAsync" />: (1) build a
///         <see cref="NinlilDnsContext" /> against the target engine/connection string and
///         apply every pending migration to it, proving the target is reachable and ready while
///         the current engine still serves every live request completely untouched; (2) take a
///         real, full-system backup of the still-active current engine via
///         <see cref="IBackupService" />; (3) flip the shared <see cref="StorageOptions" />
///         singleton onto the target, the one moment this process's own live storage genuinely
///         changes; (4) restore the backup captured in step 2 into the target, whose own
///         dependency-injection scope -- opened only after step 3 -- already resolves against it.
///         A failure at any point after step 3 reverts the singleton back to the engine that was
///         active before this call began, so the previously active engine -- whose own data this
///         class never touches -- remains authoritative; a failure before step 3 never touches
///         the shared singleton at all.
///     </para>
/// </summary>
/// <param name="currentOptions">
///     The exact <see cref="StorageOptions" /> singleton instance
///     <c>AddNinlilDnsEntityFrameworkCore</c> registered and closed its own
///     <c>AddDbContext</c> configuration delegate over -- mutating this instance in place is the
///     entire mechanism this class exists to drive.
/// </param>
/// <param name="backupService">Used to capture and restore the data actually carried across the switch.</param>
/// <param name="backupOptions">Read only for its own configured minimum passphrase length.</param>
/// <param name="state">The shared in-progress gate this class claims for the duration of a switch.</param>
/// <param name="logger">The logger this class records a switch's progress and outcome to.</param>
public sealed class StorageEngineSwitchService(
    StorageOptions currentOptions,
    IBackupService backupService,
    IOptions<BackupOptions> backupOptions,
    StorageEngineSwitchState state,
    ILogger<StorageEngineSwitchService> logger
) : IStorageEngineSwitchService
{
    /// <summary>
    ///     This constant lists every engine <see cref="SwitchEngineAsync" /> can genuinely switch
    ///     to or from without a restart -- see this class's own doc comment for why
    ///     <see cref="StorageEngineEnum.MongoDb" /> is not among them.
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

            // Step 1: provision and migrate the target while the current engine still serves
            // every live request, completely untouched -- proves the target is reachable and
            // ready before anything about this process's own live traffic changes.
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

            // Step 2: back up the still-active, still-untouched current engine. The passphrase is
            // generated fresh, held only in memory for the lifetime of this call, and never
            // returned or logged -- this archive exists purely to carry data across this one
            // switch, not as a durable backup an operator is expected to keep.
            string transientPassphrase = GenerateTransientPassphrase();
            byte[] archive = await backupService.CreateBackupAsync(
                transientPassphrase,
                stoppingToken
            );

            // Step 3: flip. Every dependency-injection scope opened from this instant forward --
            // the very next incoming request included -- resolves NinlilDnsContext against the
            // target. See this class's own doc comment for exactly why mutating this singleton in
            // place is sufficient.
            currentOptions.Engine = request.TargetEngine;
            currentOptions.ConnectionString = request.TargetConnectionString;
            flipped = true;

            // Step 4: restore the backup captured in step 2 into the target -- this call's own
            // scope is opened only now, after the flip, so every repository/unit-of-work it
            // resolves is already bound to the target engine.
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
            // Revert the flip, if it already happened, so the engine that was active before this
            // call began -- whose own data was never touched by any step above -- remains
            // authoritative. A failure before step 3 never touched the singleton at all, so this
            // is always safe to run unconditionally.
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
    ///     This method generates a random passphrase strong enough to satisfy
    ///     <see cref="BackupOptions.MinimumPassphraseLength" /> with headroom, used only to
    ///     encrypt the transient, in-memory-lifetime archive <see cref="SwitchEngineAsync" />
    ///     carries data across a switch with -- never surfaced to a caller, logged, or persisted
    ///     anywhere.
    /// </summary>
    private string GenerateTransientPassphrase()
    {
        int length = Math.Max(48, backupOptions.Value.MinimumPassphraseLength + 16);
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(length));
    }
}
