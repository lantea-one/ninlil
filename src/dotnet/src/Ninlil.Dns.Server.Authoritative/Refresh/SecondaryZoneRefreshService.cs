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
///     This class implements the secondary-zone half of RFC 1035 §4.3.5 / RFC 1996: for every
///     zone this server hosts as <see cref="ZoneTypeEnum.Secondary" />, periodically (per that
///     zone's own SOA Refresh interval) or immediately (on request, from a validated NOTIFY)
///     checks the configured primary's serial and pulls a transfer if it is ahead.
///     <para>
///         Scheduling state (when each zone is next due) is kept in memory only, not persisted:
///         a restart simply means every secondary zone is checked again right away, which is
///         always safe -- at worst, a redundant "you are already current" round trip -- never
///         incorrect.
///     </para>
/// </summary>
/// <param name="scopeFactory">The factory used to reach a fresh set of scoped services per check.</param>
/// <param name="options">
///     This service's own configuration, such as polling granularity -- bound via
///     <see cref="IOptionsMonitor{TOptions}" /> and read fresh, via <c>options.CurrentValue</c>,
///     on every single loop iteration. This class is a singleton hosted service whose own
///     <see cref="ExecuteAsync" /> runs its loop body once per process; before this fix, reading
///     it via the plain, snapshotted <see cref="IOptions{TOptions}" /> looked like it should
///     hot-reload -- the loop body already re-read the property on every iteration -- but never
///     actually did, since <see cref="IOptions{TOptions}.Value" /> is computed once and cached
///     for the life of the process, the same shape already found and fixed for
///     <c>DnssecResigningBackgroundService</c>.
/// </param>
/// <param name="logger">The logger this service records refresh failures to.</param>
public sealed class SecondaryZoneRefreshService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<SecondaryZoneRefreshOptions> options,
    ILogger<SecondaryZoneRefreshService> logger
) : BackgroundService, ISecondaryZoneRefreshTrigger
{
    /// <summary>
    ///     This property tracks, per zone, when it is next due to be checked. A zone with no
    ///     entry yet is treated as due immediately.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _nextCheckAtUtc = new();

    /// <summary>
    ///     This property is what actually makes "immediately" in <see cref="RequestImmediateRefresh" />
    ///     mean something: without it, marking a zone due would only take effect once
    ///     <see cref="ExecuteAsync" />'s own sleep between polls happened to elapse on its own,
    ///     which could be as long as <see cref="SecondaryZoneRefreshOptions.PollInterval" /> --
    ///     the opposite of "immediately" for the whole point a NOTIFY exists.
    /// </summary>
    private readonly SemaphoreSlim _wakeSignal = new(0, 1);

    /// <inheritdoc />
    public void RequestImmediateRefresh(Guid zoneId)
    {
        _nextCheckAtUtc[zoneId] = DateTimeOffset.MinValue;

        // Only release if nothing is already pending -- a request arriving while an earlier one
        // hasn't been consumed yet has nothing more to add; both just mean "wake up".
        if (_wakeSignal.CurrentCount == 0)
        {
            try
            {
                _wakeSignal.Release();
            }
            catch (SemaphoreFullException)
            {
                // Lost a race with another concurrent request already releasing it; the
                // outcome -- the loop wakes up -- is identical either way.
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
                // Waking up either because PollInterval elapsed on its own, or because
                // RequestImmediateRefresh signaled it early, are handled identically: either
                // way, loop back around and let CheckDueZonesAsync decide what is actually due.
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

        // SecondaryCatalog is polled/pulled through this exact same loop as an ordinary
        // Secondary zone -- an RFC 9432 catalog zone is, per its own spec text, "a regular DNS
        // zone", and this project extends rather than replaces this machinery for it.
        // SecondaryForwarder is included the same way: its own apex SOA/TXT content (a
        // ZoneTypeEnum.Forwarder primary's own encoded ForwarderAddresses, see
        // Forwarding.ForwarderZoneContentSyncService) is, from this loop's own point of view, an
        // ordinary AXFR/IXFR-transferable zone like any other -- only RefreshZoneAsync's own
        // post-pull step (decoding those TXT records back into this zone's own ForwarderAddresses)
        // is genuinely specific to it. Stub is included too, refreshed via a genuinely different,
        // NS-only exchange (RefreshZoneAsync's own dedicated branch) rather than a full AXFR/IXFR
        // pull. This filter is exactly the shape of place a new
        // answerable (or, for Stub, periodically refreshable) zone type is easy to leave out of by
        // mistake.
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

            // Reschedule to the default retry interval up front, in case this check throws or
            // finds nothing better to reschedule from; RefreshZoneAsync overwrites this with a
            // more accurate time whenever it actually learns one.
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
    ///     This method checks a single zone against each of its configured primaries, in order,
    ///     until one answers, pulling and applying a transfer if the primary is ahead -- or, for a
    ///     <see cref="ZoneTypeEnum.Stub" /> zone, delegating entirely to
    ///     <see cref="RefreshStubZoneAsync" /> instead, since that zone type's own refresh is a
    ///     genuinely different NS-only exchange rather than an AXFR/IXFR pull.
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

        // Selects the one registered IZoneTransferClient whose own Transport matches this zone's
        // configured preference (ZoneEntity.TransferTransport) -- see
        // ServiceCollectionAuthoritativeExtension's own doc comment for why three
        // implementations share this one interface.
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

            // A SecondaryForwarder zone's own "content", from Server.Forwarding's own point of
            // view, is not the TXT records themselves but the ForwarderAddresses list they
            // encode -- decode the zone's own freshly-pulled apex TXT records back into it now
            // that ApplyPullResultAsync has landed them as ordinary RecordEntity rows, exactly
            // the same generic AXFR/IXFR record content an ordinary Secondary zone would have
            // received. A no-op for every other zone type.
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
    ///     This method decodes a <see cref="ZoneTypeEnum.SecondaryForwarder" /> zone's own
    ///     currently-stored apex TXT records (via <see cref="ForwarderAddressTxtCodec" />) back
    ///     into its own <see cref="ZoneEntity.ForwarderAddresses" />, restoring each entry's own
    ///     original list position from the index every encoded TXT record carries -- a TXT RRset
    ///     carries no ordering guarantee of its own, but this project's own default forwarder
    ///     failover strategy depends on list order to mean "priority". A no-op for any other zone
    ///     type, and a no-op once the decoded set already matches what is already stored, so this
    ///     never writes (or bumps <c>ZoneEntity.UpdatedAtUtc</c> for) a poll that pulled no real
    ///     change.
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

        // Written directly against IZoneRepository, not through IZoneUpdateService -- there is no
        // record content to journal here (that already happened inside ApplyPullResultAsync,
        // whose own DnsZoneChangedEvent already reports this zone as changed); this is purely
        // this ZoneEntity's own metadata field catching up to content that already landed.
        IZoneRepository zoneRepository = services.GetRequiredService<IZoneRepository>();
        IUnitOfWork unitOfWork = services.GetRequiredService<IUnitOfWork>();
        zoneRepository.Update(zone);
        await unitOfWork.SaveChangesAsync(stoppingToken);
    }

    /// <summary>
    ///     This method refreshes a <see cref="ZoneTypeEnum.Stub" /> zone: learning its
    ///     delegation's current NS records (and any address glue the primary also provides) via
    ///     <see cref="StubZoneNsPuller" />, then wholesale-replacing whatever this server
    ///     previously learned with the freshly-pulled set. A stub zone has no SOA or serial of its
    ///     own to diff against -- unlike an ordinary secondary zone's incremental journal replay,
    ///     this is the same "no diff/versioning needed, just refresh the whole set" pattern
    ///     <c>DnsFilterListExternalRefreshService</c> already establishes for a periodically
    ///     refreshed, subscribed set.
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

            // Nothing actually changed since the last successful refresh -- skip the write (and
            // the DnsZoneChangedEvent publish below, which webhook subscribers among others react
            // to) entirely, rather than churning storage and firing events on every single poll.
            if (existingWireRecords.SetEquals(pullResult.Records))
            {
                // This zone's next check was already pre-scheduled to DefaultRetryInterval by
                // CheckDueZonesAsync, and a stub zone has no SOA-driven Refresh interval of its
                // own to reschedule from instead -- nothing more to do here.
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
    ///     This method applies a successful pull to local storage: bootstrapping a brand-new
    ///     zone, replaying an incremental diff step by step, or reconciling a full record set
    ///     against what is already stored.
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

                // A record the operator has marked ExcludeFromSync is local-only by definition,
                // and never appeared in whatever the primary sent that produced this step -- but
                // if one happens to coincide by name/type/data with something the primary really
                // did remove, it must still never be deleted here.
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

            // The SOA record itself is excluded from this diff -- ApplyChangesAsync already
            // advances the zone's existing SOA row to explicitNewSerial on its own, so treating
            // the incoming SOA as just another record to reconcile would insert a duplicate one.
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

            // Every ExcludeFromSync record is local-only, so it will always look "missing from
            // the primary's own full record set" by this diff's own logic -- excluded here so
            // that never gets mistaken for something the primary actually deleted.
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
    ///     This method reads the zone's own current SOA serial, or <see langword="null" /> if it
    ///     has no local copy at all yet.
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
    ///     This method schedules the zone's next check, per its own SOA Refresh interval.
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
    ///     This method finds the stored record a wire-form record (named for removal in an
    ///     incremental diff) actually corresponds to.
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
