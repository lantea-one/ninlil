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
///     This class keeps every <see cref="ZoneTypeEnum.CloudPrimary" />/<see cref="ZoneTypeEnum.CloudSecondary" />
///     zone in sync with its configured cloud DNS provider -- the REST-polling analogue of
///     <c>SecondaryZoneRefreshService</c>'s own AXFR/IXFR refresh loop, reconciling by record
///     content (the same technique that service's own full-transfer branch uses) rather than by
///     any provider-assigned record ID, since three different providers have three different,
///     mutually incompatible ID schemes.
/// </summary>
/// <param name="scopeFactory">The factory used to reach every scoped repository this service needs, once per zone, per tick.</param>
/// <param name="providers">Every registered <see cref="ICloudDnsProvider" /> implementation.</param>
/// <param name="protector">The protector used to decrypt a zone's own credential set.</param>
/// <param name="logger">The logger this service records per-zone sync failures to.</param>
/// <param name="eventPublisher">
///     The publisher this service raises <see cref="CloudDnsZoneSyncFailedEvent" /> through for
///     a single zone's own sync failure -- optional, defaulting to <see langword="null" />, the
///     same pattern <c>SignedZoneCache</c>'s/<c>AcmeCertificateRenewalService</c>'s own new
///     <c>eventPublisher</c> parameters follow, so every pre-existing caller/test keeps
///     compiling and behaving unchanged.
/// </param>
public sealed class CloudDnsZoneSyncService(
    IServiceScopeFactory scopeFactory,
    IEnumerable<ICloudDnsProvider> providers,
    CloudCredentialProtector protector,
    ILogger<CloudDnsZoneSyncService> logger,
    IDnsServerEventPublisher eventPublisher = null
) : BackgroundService
{
    /// <summary>
    ///     This constant contains how often this service wakes up to check whether any zone is
    ///     actually due for a sync -- independent of, and shorter than, any single zone's own
    ///     <see cref="ZoneEntity.CloudSyncInterval" />, so a zone configured with a short
    ///     interval is not left waiting behind one configured with a much longer one.
    /// </summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     This property tracks, per zone, when this service last attempted a sync -- kept in
    ///     memory only; a restart simply syncs every zone again immediately, which is harmless
    ///     since this reconciliation is idempotent.
    /// </summary>
    private readonly Dictionary<Guid, DateTimeOffset> _lastSyncedAtUtc = [];

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
                // One failed tick (a transient failure reaching every zone's repository, for
                // example) must never take this whole background service down -- there is
                // always another tick TickInterval later.
                logger.LogWarning(exception, "A cloud DNS sync tick failed unexpectedly.");
            }

            try
            {
                await Task.Delay(TickInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    ///     This method finds every enabled cloud-synced zone that is actually due, and syncs
    ///     each in turn.
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

        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (ZoneEntity zone in zones)
        {
            bool isDue =
                !_lastSyncedAtUtc.TryGetValue(zone.Id, out DateTimeOffset lastSyncedAtUtc)
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
                // A single zone's own sync failure (an expired token, a provider outage, a
                // misconfigured zone ID) must never block every other zone's own sync.
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
    }

    /// <summary>
    ///     This method reconciles a single zone against its configured cloud provider: pushing
    ///     local changes out for <see cref="ZoneTypeEnum.CloudPrimary" />, or pulling the
    ///     provider's own changes in for <see cref="ZoneTypeEnum.CloudSecondary" />.
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

        IReadOnlyList<CloudDnsProviderRecord> remoteRecords = await provider.ListRecordsAsync(
            credentials,
            zone.CloudProviderZoneId,
            stoppingToken
        );

        IReadOnlyList<RecordEntity> localRecords = await recordRepository.ListByZoneIdAsync(
            zone.Id,
            stoppingToken
        );

        if (localRecords.Count == 0 && zone.Type == ZoneTypeEnum.CloudSecondary)
        {
            // Unlike an RFC 5936 AXFR (which transfers the SOA itself, letting
            // SecondaryZoneRefreshService bootstrap a brand-new secondary zone from nothing),
            // this reconciliation deliberately never touches SOA/apex-NS records (see the type
            // scoping note on ICloudDnsProvider) -- so a CloudSecondary zone needs its own
            // locally-seeded SOA record before there is anywhere for ApplyChangesAsync to record
            // a serial bump against. Skip, with a clear message, rather than let
            // ApplyChangesAsync throw its own less specific "no SOA record" exception.
            logger.LogWarning(
                "Zone '{ZoneName}' is a new CloudSecondary zone with no local records yet -- seed it with its own SOA record before cloud sync can populate the rest.",
                zone.Name
            );
            return;
        }

        // The zone's own apex SOA/NS records are managed by whichever side owns the zone's own
        // delegation (the registrar, for a cloud provider; ApplyChangesAsync itself, locally) --
        // neither is ever something this reconciliation should create, delete, or even consider,
        // the same way a real cloud provider's own console does not let an operator edit them.
        // ExcludeFromSync records are, by definition, never part of this either.
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
    ///     This method pushes this server's own local changes out to the provider, for a
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
    ///     This method pulls the provider's own changes into local storage, for a
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
