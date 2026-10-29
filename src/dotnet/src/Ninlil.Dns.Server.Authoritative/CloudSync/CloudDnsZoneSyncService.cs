using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Authoritative.Journal;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.CloudSync;

/// <summary>
///     This hosted service keeps each <see cref="ZoneTypeEnum.CloudPrimary" /> and
///     <see cref="ZoneTypeEnum.CloudSecondary" /> zone in step with its cloud DNS provider, on the
///     zone's <see cref="ZoneEntity.CloudSyncInterval" />. CloudPrimary zones push local records to
///     the provider; CloudSecondary zones pull the provider's records in through
///     <see cref="IZoneUpdateService" />. Records are matched by content, like the full-transfer
///     branch of <c>SecondaryZoneRefreshService</c>, because each provider has its record IDs.
///     The SOA, apex NS and ExcludeFromSync records are never synced, and the provider's own apex NS
///     and SOA records are ignored on both sides.
///     It also implements <see cref="ICloudZoneSyncTrigger" />, so a caller can sync one zone at
///     once (<see cref="SyncZoneNowAsync" />, which waits) or ask for a sync without waiting
///     (<see cref="RequestSync" />, which marks the zone and wakes the loop). A semaphore lets only
///     one sync run at a time, whether it comes from the loop or from the trigger, so a manual
///     push never overlaps a loop pass. The dependency injection container registers one instance
///     that serves as the trigger and as the hosted service.
/// </summary>
/// <param name="scopeFactory">The factory used to create a scope per zone sync.</param>
/// <param name="providers">Every registered <see cref="ICloudDnsProvider" /> implementation.</param>
/// <param name="protector">The protector used to decrypt a zone's credentials.</param>
/// <param name="logger">The logger this service records per-zone sync failures to.</param>
/// <param name="eventPublisher">
///     The publisher for <see cref="CloudDnsZoneSyncFailedEvent" /> when a zone fails to sync.
///     With <see langword="null" />, nothing is published.
/// </param>
public sealed class CloudDnsZoneSyncService(
    IServiceScopeFactory scopeFactory,
    IEnumerable<ICloudDnsProvider> providers,
    CloudCredentialProtector protector,
    ILogger<CloudDnsZoneSyncService> logger,
    IDnsServerEventPublisher eventPublisher = null
) : BackgroundService, ICloudZoneSyncTrigger
{
    /// <summary>
    ///     This field contains how often the service checks for zones due a sync. It is shorter than
    ///     any zone's <see cref="ZoneEntity.CloudSyncInterval" />, so short intervals are honoured.
    /// </summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     This field records when each zone was last synced, in memory only; after a restart every
    ///     zone syncs at once, which is harmless because syncing is idempotent.
    /// </summary>
    private readonly Dictionary<Guid, DateTimeOffset> _lastSyncedAtUtc = [];

    /// <summary>
    ///     This field lets one zone sync run at a time. The loop and
    ///     <see cref="SyncZoneNowAsync" /> both take it around every sync, and
    ///     <see cref="_lastSyncedAtUtc" /> is read and written only while it is held.
    /// </summary>
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    /// <summary>
    ///     This field holds the zones <see cref="RequestSync" /> asked for and the loop has not yet
    ///     started to sync. It is a set: the value is unused.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, byte> _pending = new();

    /// <summary>
    ///     This field is the wake signal of the loop. <see cref="RequestSync" /> releases it, and
    ///     the loop waits on it instead of sleeping for the whole tick. Its count stays at 0 or 1
    ///     in practice: one release is enough to start a pass, and the pass serves every zone
    ///     marked in <see cref="_pending" /> by then.
    /// </summary>
    private readonly SemaphoreSlim _wake = new(0);

    /// <summary>
    ///     This field backs <see cref="CompletedPassCount" />; the loop increments it with
    ///     <see cref="Interlocked" />.
    /// </summary>
    private long _completedPassCount;

    /// <inheritdoc />
    public async Task SyncZoneNowAsync(Guid zoneId, CancellationToken stoppingToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        ZoneEntity zone = await scope
            .ServiceProvider.GetRequiredService<IZoneRepository>()
            .GetByIdAsync(zoneId, stoppingToken);

        if (
            zone is null
            || zone.IsDisabled
            || (zone.Type != ZoneTypeEnum.CloudPrimary && zone.Type != ZoneTypeEnum.CloudSecondary)
        )
            return;

        await _syncLock.WaitAsync(stoppingToken);

        try
        {
            await SyncZoneAsync(zone, stoppingToken);
            _lastSyncedAtUtc[zone.Id] = DateTimeOffset.UtcNow;
        }
        finally
        {
            _syncLock.Release();
        }
    }

    /// <summary>
    ///     This property contains the number of loop passes that have ended, by finishing or by a
    ///     failure the loop logged. A pass cut short by the service stopping is not counted. Tests
    ///     wait on it to know a pass has ended instead of guessing how long it takes.
    /// </summary>
    internal long CompletedPassCount => Interlocked.Read(ref _completedPassCount);

    /// <inheritdoc />
    public void RequestSync(Guid zoneId)
    {
        // Mark first, then signal: a loop woken by the signal must find the mark. A request that
        // lands while its zone is syncing marks it again, so the change that caused the request is
        // pushed by the pass after the one in flight, never lost. A request that lands before the
        // zone's sync starts joins that sync, so a burst costs at most two syncs.
        _pending[zoneId] = 0;

        // The signal is skipped when one is already waiting. The check is not atomic with the
        // release, so two racing callers can both release; the cost is one extra pass that finds
        // nothing due.
        if (_wake.CurrentCount == 0)
            _wake.Release();
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncDueZonesAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                // A failed tick must not stop the service; the next tick tries again.
                logger.LogWarning(exception, "A cloud DNS sync tick failed unexpectedly.");
            }

            Interlocked.Increment(ref _completedPassCount);

            try
            {
                // Ends at the next tick or as soon as RequestSync signals.
                await _wake.WaitAsync(TickInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    ///     This method syncs every enabled cloud zone that is due, one at a time.
    /// </summary>
    private async Task SyncDueZonesAsync(CancellationToken stoppingToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IZoneRepository zoneRepository =
            scope.ServiceProvider.GetRequiredService<IZoneRepository>();

        IReadOnlyList<ZoneEntity> zones = await zoneRepository.ListAsync(
            zone =>
                !zone.IsDisabled
                && (
                    zone.Type == ZoneTypeEnum.CloudPrimary
                    || zone.Type == ZoneTypeEnum.CloudSecondary
                ),
            stoppingToken
        );

        // A request for a zone that is not in this list (unknown, disabled or not a cloud zone) is
        // dropped here, so such ids never pile up and a zone enabled later does not start with a
        // stale request.
        HashSet<Guid> listedZoneIds = [.. zones.Select(zone => zone.Id)];

        foreach (Guid pendingZoneId in _pending.Keys)
        {
            if (!listedZoneIds.Contains(pendingZoneId))
                _pending.TryRemove(pendingZoneId, out _);
        }

        foreach (ZoneEntity zone in zones)
        {
            // Held across the due check and the sync, so an on-demand sync of the same zone
            // either finishes first (and this pass sees it as recent) or waits for this one.
            await _syncLock.WaitAsync(stoppingToken);

            try
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;

                // The mark is taken here, under the lock and just before the sync starts. A request
                // that arrives from now on marks the zone again and is served by the next pass.
                bool isRequested = _pending.TryRemove(zone.Id, out _);
                bool isDue =
                    isRequested
                    || !_lastSyncedAtUtc.TryGetValue(zone.Id, out DateTimeOffset lastSyncedAtUtc)
                    || now - lastSyncedAtUtc >= zone.CloudSyncInterval;

                if (!isDue)
                    continue;

                _lastSyncedAtUtc[zone.Id] = now;

                try
                {
                    await SyncZoneAsync(zone, stoppingToken);
                }
                catch (Exception exception)
                {
                    // One zone's failure (an expired token, an outage, a wrong zone ID) must not block the
                    // others.
                    logger.LogWarning(
                        exception,
                        "Cloud DNS sync failed for zone '{ZoneName}'.",
                        zone.Name
                    );

                    eventPublisher?.Publish(
                        new CloudDnsZoneSyncFailedEvent(
                            DateTimeOffset.UtcNow,
                            zone.Id,
                            zone.Name,
                            exception.Message
                        )
                    );
                }
            }
            finally
            {
                _syncLock.Release();
            }
        }
    }

    /// <summary>
    ///     This method syncs one zone with its provider: pushing for
    ///     <see cref="ZoneTypeEnum.CloudPrimary" />, pulling for <see cref="ZoneTypeEnum.CloudSecondary" />.
    /// </summary>
    private async Task SyncZoneAsync(ZoneEntity zone, CancellationToken stoppingToken)
    {
        if (
            zone.CloudProviderCredentialId is null
            || string.IsNullOrWhiteSpace(zone.CloudProviderZoneId)
        )
        {
            logger.LogWarning(
                "Zone '{ZoneName}' is cloud-synced but has no credential or provider zone ID configured; skipping.",
                zone.Name
            );
            return;
        }

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        ICloudDnsProviderCredentialRepository credentialRepository =
            scope.ServiceProvider.GetRequiredService<ICloudDnsProviderCredentialRepository>();
        IRecordRepository recordRepository =
            scope.ServiceProvider.GetRequiredService<IRecordRepository>();
        IZoneUpdateService zoneUpdateService =
            scope.ServiceProvider.GetRequiredService<IZoneUpdateService>();

        CloudDnsProviderCredentialEntity credential = await credentialRepository.GetByIdAsync(
            zone.CloudProviderCredentialId.Value,
            stoppingToken
        );

        if (credential is null || credential.IsDisabled)
        {
            logger.LogWarning(
                "Zone '{ZoneName}' references a missing or disabled cloud DNS provider credential; skipping.",
                zone.Name
            );
            return;
        }

        ICloudDnsProvider provider = providers.FirstOrDefault(candidate =>
            candidate.ProviderType == credential.ProviderType
        );

        if (provider is null)
        {
            logger.LogWarning(
                "No ICloudDnsProvider is registered for provider type '{ProviderType}'; skipping zone '{ZoneName}'.",
                credential.ProviderType,
                zone.Name
            );
            return;
        }

        IReadOnlyDictionary<string, string> credentials = protector.Unprotect(
            credential.CredentialsEncrypted
        );

        // The provider owns its apex NS set (Route 53 refuses to delete it) and its SOA. Dropping
        // them here, before either direction runs, means a CloudPrimary never deletes them and a
        // CloudSecondary never re-adds them locally on every pass.
        IReadOnlyList<CloudDnsProviderRecord> remoteRecords =
        [
            .. (
                await provider.ListRecordsAsync(
                    credentials,
                    zone.CloudProviderZoneId,
                    stoppingToken
                )
            ).Where(remote =>
                remote.Record.Type != DnsResourceRecordTypeEnum.Soa
                && !(
                    remote.Record.Type == DnsResourceRecordTypeEnum.NS
                    && string.Equals(
                        remote.Record.Name.ToString().TrimEnd('.'),
                        zone.Name.TrimEnd('.'),
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            ),
        ];

        IReadOnlyList<RecordEntity> localRecords = await recordRepository.ListByZoneIdAsync(
            zone.Id,
            stoppingToken
        );

        if (localRecords.Count == 0 && zone.Type == ZoneTypeEnum.CloudSecondary)
        {
            // Unlike AXFR, this sync never transfers the SOA, so a CloudSecondary zone needs a local SOA
            // before ApplyChangesAsync can bump its serial. Skip with a clear message instead of letting
            // ApplyChangesAsync throw a vaguer error.
            logger.LogWarning(
                "Zone '{ZoneName}' is a new CloudSecondary zone with no local records yet -- seed it with its own SOA record before cloud sync can populate the rest.",
                zone.Name
            );
            return;
        }

        // The apex SOA and NS records belong to each side (the registrar or provider there,
        // ApplyChangesAsync here), so they are never created or deleted by the sync, and neither
        // are ExcludeFromSync records.
        List<(RecordEntity Entity, DnsResourceRecord Wire)> syncableLocal =
        [
            .. localRecords
                .Where(record =>
                    !record.ExcludeFromSync
                    && CloudDnsSyncSupportedRecordTypes.Types.Contains(record.Type)
                    && !(
                        record.Type == DnsResourceRecordTypeEnum.NS
                        && string.Equals(record.Name, zone.Name, StringComparison.OrdinalIgnoreCase)
                    )
                )
                .Select(record =>
                    (record, RecordEntityCodec.ToWireRecord(record, DnsName.Parse(record.Name)))
                )
                .Where(pair => pair.Item2 is not null),
        ];

        HashSet<DnsResourceRecord> localSet = [.. syncableLocal.Select(pair => pair.Wire)];
        HashSet<DnsResourceRecord> remoteSet = [.. remoteRecords.Select(remote => remote.Record)];

        if (zone.Type == ZoneTypeEnum.CloudPrimary)
        {
            await PushLocalChangesAsync(
                provider,
                credentials,
                zone,
                syncableLocal,
                remoteRecords,
                localSet,
                remoteSet,
                stoppingToken
            );
        }
        else
        {
            await PullRemoteChangesAsync(
                zoneUpdateService,
                zone,
                syncableLocal,
                remoteRecords,
                localSet,
                remoteSet,
                stoppingToken
            );
        }
    }

    /// <summary>
    ///     This method pushes local changes to the provider, for a
    ///     <see cref="ZoneTypeEnum.CloudPrimary" /> zone.
    /// </summary>
    private static async Task PushLocalChangesAsync(
        ICloudDnsProvider provider,
        IReadOnlyDictionary<string, string> credentials,
        ZoneEntity zone,
        List<(RecordEntity Entity, DnsResourceRecord Wire)> syncableLocal,
        IReadOnlyList<CloudDnsProviderRecord> remoteRecords,
        HashSet<DnsResourceRecord> localSet,
        HashSet<DnsResourceRecord> remoteSet,
        CancellationToken stoppingToken
    )
    {
        foreach ((RecordEntity Entity, DnsResourceRecord Wire) pair in syncableLocal)
            if (!remoteSet.Contains(pair.Wire))
                await provider.CreateRecordAsync(
                    credentials,
                    zone.CloudProviderZoneId,
                    pair.Wire,
                    stoppingToken
                );

        foreach (CloudDnsProviderRecord remote in remoteRecords)
            if (
                CloudDnsSyncSupportedRecordTypes.Types.Contains(remote.Record.Type)
                && !localSet.Contains(remote.Record)
            )
                await provider.DeleteRecordAsync(
                    credentials,
                    zone.CloudProviderZoneId,
                    remote,
                    stoppingToken
                );
    }

    /// <summary>
    ///     This method pulls the provider's changes into local storage, for a
    ///     <see cref="ZoneTypeEnum.CloudSecondary" /> zone.
    /// </summary>
    private static async Task PullRemoteChangesAsync(
        IZoneUpdateService zoneUpdateService,
        ZoneEntity zone,
        List<(RecordEntity Entity, DnsResourceRecord Wire)> syncableLocal,
        IReadOnlyList<CloudDnsProviderRecord> remoteRecords,
        HashSet<DnsResourceRecord> localSet,
        HashSet<DnsResourceRecord> remoteSet,
        CancellationToken stoppingToken
    )
    {
        List<RecordEntity> toAdd =
        [
            .. remoteRecords
                .Where(remote =>
                    CloudDnsSyncSupportedRecordTypes.Types.Contains(remote.Record.Type)
                    && !localSet.Contains(remote.Record)
                )
                .Select(remote => RecordEntityCodec.FromWireRecord(remote.Record, zone.Id))
                .Where(record => record is not null),
        ];

        List<RecordEntity> toRemove =
        [
            .. syncableLocal
                .Where(pair => !remoteSet.Contains(pair.Wire))
                .Select(pair => pair.Entity),
        ];

        if (toAdd.Count > 0 || toRemove.Count > 0)
            await zoneUpdateService.ApplyChangesAsync(
                zone.Id,
                toAdd,
                toRemove,
                null,
                stoppingToken
            );
    }
}
