using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Authoritative.Journal;
using Ninlil.Dns.Server.Authoritative.Notify;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Forwarding;
using Ninlil.Dns.Server.Tsig;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Refresh;

/// <summary>
///     This hosted service keeps secondary zones current (RFC 1035 §4.3.5, RFC 1996). For each
///     Secondary, SecondaryCatalog, SecondaryForwarder and Stub zone, on its SOA Refresh interval
///     or at once after a NOTIFY (<see cref="ISecondaryZoneRefreshTrigger" />), it asks the
///     primaries for their serial and pulls an AXFR/IXFR through the zone's
///     <see cref="IZoneTransferClient" /> when a primary is ahead, applying it through
///     <see cref="IZoneUpdateService" />. Stub zones get an NS query instead. The SOA Expire field
///     is not used.
///     <para>
///         When each zone is next due is kept in memory only; after a restart every zone is checked
///         at once, which at worst costs one "already current" round trip.
///     </para>
/// </summary>
/// <param name="scopeFactory">The factory used to create a scope per check.</param>
/// <param name="options">
///     The poll interval and retry delay, read through <see cref="IOptionsMonitor{TOptions}" /> on
///     every iteration; <see cref="IOptions{TOptions}.Value" /> would keep the startup value in
///     this singleton.
/// </param>
/// <param name="logger">The logger this service records refresh failures to.</param>
public sealed class SecondaryZoneRefreshService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<SecondaryZoneRefreshOptions> options,
    ILogger<SecondaryZoneRefreshService> logger
) : BackgroundService, ISecondaryZoneRefreshTrigger
{
    /// <summary>
    ///     This field records when each zone is next due. A zone with no entry is due now.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _nextCheckAtUtc = new();

    /// <summary>
    ///     This field lets <see cref="RequestImmediateRefresh" /> wake the loop at once instead of
    ///     after up to <see cref="SecondaryZoneRefreshOptions.PollInterval" />.
    /// </summary>
    private readonly SemaphoreSlim _wakeSignal = new(0, 1);

    /// <inheritdoc />
    public void RequestImmediateRefresh(Guid zoneId)
    {
        _nextCheckAtUtc[zoneId] = DateTimeOffset.MinValue;

        // Release only if no wake-up is pending; a second request adds nothing.
        if (_wakeSignal.CurrentCount == 0)
        {
            try
            {
                _wakeSignal.Release();
            }
            catch (SemaphoreFullException)
            {
                // Another request released it first; the loop wakes up either way.
            }
        }
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckDueZonesAsync(stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Unhandled exception while checking secondary zones.");
            }

            try
            {
                // Woken by the interval or by RequestImmediateRefresh: either way,
                // CheckDueZonesAsync decides what is due.
                await _wakeSignal.WaitAsync(options.CurrentValue.PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    ///     This method checks every secondary zone whose next-check time has come due.
    /// </summary>
    private async Task CheckDueZonesAsync(CancellationToken stoppingToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IZoneRepository zoneRepository =
            scope.ServiceProvider.GetRequiredService<IZoneRepository>();

        // SecondaryCatalog zones (ordinary zones under RFC 9432) and SecondaryForwarder zones
        // (their apex records are transferred like any zone; see
        // Forwarding.ForwarderZoneContentSyncService) are pulled like Secondary zones. Stub zones
        // use an NS query instead (RefreshStubZoneAsync). A new refreshable zone type must be added
        // here.
        IReadOnlyList<ZoneEntity> secondaryZones = await zoneRepository.ListAsync(
            zone =>
                !zone.IsDisabled
                && (
                    zone.Type == ZoneTypeEnum.Secondary
                    || zone.Type == ZoneTypeEnum.SecondaryCatalog
                    || zone.Type == ZoneTypeEnum.SecondaryForwarder
                    || zone.Type == ZoneTypeEnum.Stub
                ),
            stoppingToken
        );

        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (ZoneEntity zone in secondaryZones)
        {
            if (_nextCheckAtUtc.GetOrAdd(zone.Id, DateTimeOffset.MinValue) > now)
                continue;

            // Schedule the default retry first, in case the check throws; RefreshZoneAsync replaces it
            // when it learns a better time.
            _nextCheckAtUtc[zone.Id] = now + options.CurrentValue.DefaultRetryInterval;

            try
            {
                await RefreshZoneAsync(zone, scope.ServiceProvider, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(
                    exception,
                    "Unhandled exception while refreshing zone '{ZoneName}'.",
                    zone.Name
                );
            }
        }
    }

    /// <summary>
    ///     This method checks a zone against its primaries in order until one answers, and pulls and
    ///     applies a transfer if that primary is ahead. Stub zones go to
    ///     <see cref="RefreshStubZoneAsync" />.
    /// </summary>
    private async Task RefreshZoneAsync(
        ZoneEntity zone,
        IServiceProvider services,
        CancellationToken stoppingToken
    )
    {
        if (zone.Type == ZoneTypeEnum.Stub)
        {
            await RefreshStubZoneAsync(zone, services, stoppingToken);
            return;
        }

        if (zone.PrimaryServerAddresses.Count == 0)
        {
            logger.LogWarning(
                "Secondary zone '{ZoneName}' has no configured primary server addresses.",
                zone.Name
            );
            return;
        }

        // Pick the IZoneTransferClient whose Transport matches ZoneEntity.TransferTransport.
        IZoneTransferClient transferClient = services
            .GetRequiredService<IEnumerable<IZoneTransferClient>>()
            .First(client => client.Transport == zone.TransferTransport);
        IRecordRepository recordRepository = services.GetRequiredService<IRecordRepository>();
        IZoneUpdateService zoneUpdateService = services.GetRequiredService<IZoneUpdateService>();
        DnsName zoneName = DnsName.Parse(zone.Name);

        foreach (string primaryAddressText in zone.PrimaryServerAddresses)
        {
            if (!IPAddress.TryParse(primaryAddressText, out IPAddress primaryAddress))
            {
                logger.LogWarning(
                    "Secondary zone '{ZoneName}' has an invalid primary server address '{Address}'.",
                    zone.Name,
                    primaryAddressText
                );
                continue;
            }

            uint? localSerial = await ReadLocalSerialAsync(zone, recordRepository, stoppingToken);

            uint? primarySerial = await transferClient.QuerySoaSerialAsync(
                primaryAddress,
                zoneName,
                zone.TsigKeyName,
                stoppingToken
            );

            if (primarySerial is null)
            {
                logger.LogWarning(
                    "Could not reach primary {Address} for zone '{ZoneName}'.",
                    primaryAddress,
                    zone.Name
                );
                continue;
            }

            if (localSerial is { } existingSerial && existingSerial >= primarySerial)
            {
                await RescheduleFromLocalSoaAsync(zone, recordRepository, stoppingToken);
                return;
            }

            ZoneTransferPullResult pullResult = await transferClient.PullAsync(
                primaryAddress,
                zoneName,
                localSerial,
                zone.TsigKeyName,
                stoppingToken
            );

            if (!pullResult.Succeeded)
            {
                logger.LogWarning(
                    "Pulling zone '{ZoneName}' from {Address} failed: {Reason}",
                    zone.Name,
                    primaryAddress,
                    pullResult.FailureReason
                );
                continue;
            }

            await ApplyPullResultAsync(
                zone,
                localSerial,
                pullResult,
                recordRepository,
                zoneUpdateService,
                stoppingToken
            );

            // For a SecondaryForwarder zone, decode the pulled apex TXT records back into
            // ForwarderAddresses, which is what Server.Forwarding uses. Does nothing for other types.
            await SyncForwarderAddressesFromRecordsAsync(
                zone,
                recordRepository,
                services,
                stoppingToken
            );

            await RescheduleFromLocalSoaAsync(zone, recordRepository, stoppingToken);
            return;
        }
    }

    /// <summary>
    ///     This method decodes a <see cref="ZoneTypeEnum.SecondaryForwarder" /> zone's stored apex TXT
    ///     records (<see cref="ForwarderAddressTxtCodec" />) into its
    ///     <see cref="ZoneEntity.ForwarderAddresses" />, in the order the encoded indexes give, since list
    ///     order is failover priority. It does nothing for other types, or when the list is unchanged,
    ///     so a poll with no change writes nothing.
    /// </summary>
    private static async Task SyncForwarderAddressesFromRecordsAsync(
        ZoneEntity zone,
        IRecordRepository recordRepository,
        IServiceProvider services,
        CancellationToken stoppingToken
    )
    {
        if (zone.Type != ZoneTypeEnum.SecondaryForwarder)
            return;

        IReadOnlyList<RecordEntity> txtRecords = await recordRepository.FindByNameAsync(
            zone.Id,
            zone.Name,
            DnsResourceRecordTypeEnum.Txt,
            stoppingToken
        );

        List<(int Index, string Address)> decoded =
        [
            .. txtRecords
                .Select(record => RecordEntityCodec.DecodeTypedData(record))
                .OfType<TxtRecordData>()
                .Select(data =>
                    (
                        Decoded: ForwarderAddressTxtCodec.TryDecode(
                            data,
                            out int index,
                            out string address
                        ),
                        Index: index,
                        Address: address
                    )
                )
                .Where(triple => triple.Decoded)
                .Select(triple => (triple.Index, triple.Address)),
        ];

        List<string> newAddresses =
        [
            .. decoded.OrderBy(entry => entry.Index).Select(entry => entry.Address),
        ];

        if (newAddresses.SequenceEqual(zone.ForwarderAddresses))
            return;

        zone.ForwarderAddresses = newAddresses;
        zone.UpdatedAtUtc = DateTimeOffset.UtcNow;

        // Written through IZoneRepository, not IZoneUpdateService: the records were already
        // journaled by ApplyPullResultAsync; this only updates the zone's field.
        IZoneRepository zoneRepository = services.GetRequiredService<IZoneRepository>();
        IUnitOfWork unitOfWork = services.GetRequiredService<IUnitOfWork>();
        zoneRepository.Update(zone);
        await unitOfWork.SaveChangesAsync(stoppingToken);
    }

    /// <summary>
    ///     This method refreshes a <see cref="ZoneTypeEnum.Stub" /> zone: it fetches the NS records
    ///     and any glue with <see cref="StubZoneNsPuller" /> and replaces the stored set. A stub zone
    ///     has no SOA or serial, so there is no diff; the whole set is replaced when it changed.
    /// </summary>
    private async Task RefreshStubZoneAsync(
        ZoneEntity zone,
        IServiceProvider services,
        CancellationToken stoppingToken
    )
    {
        if (zone.PrimaryServerAddresses.Count == 0)
        {
            logger.LogWarning(
                "Stub zone '{ZoneName}' has no configured primary server addresses.",
                zone.Name
            );
            return;
        }

        ITsigService tsigService = services.GetRequiredService<ITsigService>();
        IRecordRepository recordRepository = services.GetRequiredService<IRecordRepository>();
        IUnitOfWork unitOfWork = services.GetRequiredService<IUnitOfWork>();
        IDnsServerEventPublisher eventPublisher =
            services.GetRequiredService<IDnsServerEventPublisher>();
        ZoneTransferClientOptions transferOptions = services
            .GetRequiredService<IOptionsMonitor<ZoneTransferClientOptions>>()
            .CurrentValue;
        DnsName zoneName = DnsName.Parse(zone.Name);

        foreach (string primaryAddressText in zone.PrimaryServerAddresses)
        {
            if (!IPAddress.TryParse(primaryAddressText, out IPAddress primaryAddress))
            {
                logger.LogWarning(
                    "Stub zone '{ZoneName}' has an invalid primary server address '{Address}'.",
                    zone.Name,
                    primaryAddressText
                );
                continue;
            }

            StubZoneNsPullResult pullResult = await StubZoneNsPuller.PullNsRecordsAsync(
                primaryAddress,
                zoneName,
                zone.TsigKeyName,
                transferOptions.PrimaryPort,
                transferOptions.SoaQueryTimeout,
                transferOptions.ConnectTimeout,
                transferOptions.ReadTimeout,
                tsigService,
                stoppingToken
            );

            if (!pullResult.Succeeded)
            {
                logger.LogWarning(
                    "Learning the delegation NS records for stub zone '{ZoneName}' from {Address} failed: {Reason}",
                    zone.Name,
                    primaryAddress,
                    pullResult.FailureReason
                );
                continue;
            }

            IReadOnlyList<RecordEntity> existingRecords = await recordRepository.ListByZoneIdAsync(
                zone.Id,
                stoppingToken
            );

            HashSet<DnsResourceRecord> existingWireRecords =
            [
                .. existingRecords.Select(record =>
                    RecordEntityCodec.ToWireRecord(record, DnsName.Parse(record.Name))
                ),
            ];

            // Nothing changed: skip the write and the DnsZoneChangedEvent, so polls do not churn
            // storage or fire webhooks.
            if (existingWireRecords.SetEquals(pullResult.Records))
            {
                // The next check is already set to DefaultRetryInterval; a stub zone has no SOA Refresh to
                // use instead.
                return;
            }

            foreach (RecordEntity existingRecord in existingRecords)
                recordRepository.Remove(existingRecord);

            foreach (DnsResourceRecord wireRecord in pullResult.Records)
            {
                RecordEntity entity = RecordEntityCodec.FromWireRecord(wireRecord, zone.Id);

                if (entity is not null)
                    await recordRepository.AddAsync(entity, stoppingToken);
            }

            await unitOfWork.SaveChangesAsync(stoppingToken);
            eventPublisher.Publish(
                new DnsZoneChangedEvent(DateTimeOffset.UtcNow, zone.Id, zone.Name)
            );
            return;
        }
    }

    /// <summary>
    ///     This method applies a successful pull: it bootstraps a new zone, replays an incremental
    ///     diff step by step, or reconciles a full record set with what is stored.
    /// </summary>
    private async Task ApplyPullResultAsync(
        ZoneEntity zone,
        uint? localSerial,
        ZoneTransferPullResult pullResult,
        IRecordRepository recordRepository,
        IZoneUpdateService zoneUpdateService,
        CancellationToken stoppingToken
    )
    {
        if (pullResult.IsAlreadyCurrent)
            return;

        if (localSerial is null)
        {
            if (pullResult.IsIncremental)
            {
                logger.LogWarning(
                    "Primary answered an initial transfer for zone '{ZoneName}' with an unexpected incremental response; skipping this refresh.",
                    zone.Name
                );
                return;
            }

            List<RecordEntity> allRecords =
            [
                .. pullResult
                    .FullRecordSet.Select(record =>
                        RecordEntityCodec.FromWireRecord(record, zone.Id)
                    )
                    .Where(record => record is not null),
            ];

            await zoneUpdateService.BootstrapZoneAsync(zone.Id, allRecords, stoppingToken);
            return;
        }

        if (pullResult.IsIncremental)
        {
            foreach (ZoneTransferDiffStep step in pullResult.DiffSteps)
            {
                IReadOnlyList<RecordEntity> currentRecords =
                    await recordRepository.ListByZoneIdAsync(zone.Id, stoppingToken);

                // An ExcludeFromSync record is local-only and never removed here, even if it matches a
                // record the primary removed.
                List<RecordEntity> toRemove =
                [
                    .. step
                        .Removed.Select(record => FindMatchingLocalRecord(currentRecords, record))
                        .Where(record => record is not null && !record.ExcludeFromSync),
                ];

                List<RecordEntity> toAdd =
                [
                    .. step
                        .Added.Select(record => RecordEntityCodec.FromWireRecord(record, zone.Id))
                        .Where(record => record is not null),
                ];

                await zoneUpdateService.ApplyChangesAsync(
                    zone.Id,
                    toAdd,
                    toRemove,
                    step.NewSerial,
                    stoppingToken
                );
            }
        }
        else
        {
            IReadOnlyList<RecordEntity> currentRecords = await recordRepository.ListByZoneIdAsync(
                zone.Id,
                stoppingToken
            );

            // Leave the SOA out of the diff: ApplyChangesAsync sets the existing SOA to
            // explicitNewSerial, and adding the incoming SOA would duplicate it.
            List<(RecordEntity Entity, DnsResourceRecord Wire)> currentNonSoa =
            [
                .. currentRecords
                    .Where(record => record.Type != DnsResourceRecordTypeEnum.Soa)
                    .Select(record =>
                        (record, RecordEntityCodec.ToWireRecord(record, DnsName.Parse(record.Name)))
                    )
                    .Where(pair => pair.Item2 is not null),
            ];

            List<DnsResourceRecord> incomingNonSoa =
            [
                .. pullResult.FullRecordSet.Where(record =>
                    record.Type != DnsResourceRecordTypeEnum.Soa
                ),
            ];

            HashSet<DnsResourceRecord> incomingSet = [.. incomingNonSoa];
            HashSet<DnsResourceRecord> currentSet = [.. currentNonSoa.Select(pair => pair.Wire)];

            // ExcludeFromSync records are local-only and always missing from the primary's set, so do
            // not treat them as deleted.
            List<RecordEntity> toRemove =
            [
                .. currentNonSoa
                    .Where(pair => !incomingSet.Contains(pair.Wire) && !pair.Entity.ExcludeFromSync)
                    .Select(pair => pair.Entity),
            ];

            List<RecordEntity> toAdd =
            [
                .. incomingNonSoa
                    .Where(record => !currentSet.Contains(record))
                    .Select(record => RecordEntityCodec.FromWireRecord(record, zone.Id))
                    .Where(record => record is not null),
            ];

            await zoneUpdateService.ApplyChangesAsync(
                zone.Id,
                toAdd,
                toRemove,
                pullResult.FinalSerial,
                stoppingToken
            );
        }
    }

    /// <summary>
    ///     This method reads the zone's current SOA serial, or <see langword="null" /> if there is
    ///     no local copy yet.
    /// </summary>
    private static async Task<uint?> ReadLocalSerialAsync(
        ZoneEntity zone,
        IRecordRepository recordRepository,
        CancellationToken stoppingToken
    )
    {
        IReadOnlyList<RecordEntity> soaRecords = await recordRepository.FindByNameAsync(
            zone.Id,
            zone.Name,
            DnsResourceRecordTypeEnum.Soa,
            stoppingToken
        );

        return
            soaRecords.Count > 0
            && RecordEntityCodec.DecodeTypedData(soaRecords[0]) is SoaRecordData soaData
            ? soaData.Serial
            : null;
    }

    /// <summary>
    ///     This method schedules the zone's next check from its SOA Refresh interval.
    /// </summary>
    private async Task RescheduleFromLocalSoaAsync(
        ZoneEntity zone,
        IRecordRepository recordRepository,
        CancellationToken stoppingToken
    )
    {
        IReadOnlyList<RecordEntity> soaRecords = await recordRepository.FindByNameAsync(
            zone.Id,
            zone.Name,
            DnsResourceRecordTypeEnum.Soa,
            stoppingToken
        );

        TimeSpan interval =
            soaRecords.Count > 0
            && RecordEntityCodec.DecodeTypedData(soaRecords[0]) is SoaRecordData soaData
            && soaData.RefreshSeconds > 0
                ? TimeSpan.FromSeconds(soaData.RefreshSeconds)
                : options.CurrentValue.DefaultRetryInterval;

        _nextCheckAtUtc[zone.Id] = DateTimeOffset.UtcNow + interval;
    }

    /// <summary>
    ///     This method finds the stored record matching a wire record that an incremental diff
    ///     removes.
    /// </summary>
    private static RecordEntity FindMatchingLocalRecord(
        IReadOnlyList<RecordEntity> currentRecords,
        DnsResourceRecord wireRecord
    )
    {
        foreach (RecordEntity candidate in currentRecords)
        {
            if (candidate.Type != wireRecord.Type)
                continue;

            DnsResourceRecord candidateWire = RecordEntityCodec.ToWireRecord(
                candidate,
                DnsName.Parse(candidate.Name)
            );

            if (candidateWire == wireRecord)
                return candidate;
        }

        return null;
    }
}
