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
///     <see cref="IZoneUpdateService" />. Stub zones get an NS query instead.
///     <para>
///         Retry: when every primary fails, a zone with a local SOA is checked again after its SOA
///         Retry interval (RFC 1035 §4.3.5); <see cref="SecondaryZoneRefreshOptions.DefaultRetryInterval" />
///         applies to a zone with no local SOA yet and to stub zones. When each zone is next due is
///         kept in memory only; after a restart every zone is checked at once, which at worst costs
///         one "already current" round trip.
///     </para>
///     <para>
///         Expire: a refresh succeeds when a primary shows the local copy is current or a pull is
///         applied. The time of that success is stored per zone in
///         <see cref="ISecondaryZoneRefreshStateRepository" /> (node-local, so it survives a
///         restart) and the zone's expiry time, that time plus the SOA Expire field, is published
///         to <see cref="SecondaryZoneExpiryTracker" />, which makes the resolver and the transfer
///         service answer SERVFAIL for the zone once it passes. SOA Expire 0 means the zone never
///         expires (a Warning at load). <see cref="StartAsync" /> loads the state before the loop
///         starts, so the host starts its listeners with the expiry already known; a zone with a
///         local SOA and no stored time (the first start, a restore, an engine switch, a zone
///         enabled after a disable) begins a fresh interval at that moment. A row whose zone is
///         deleted, disabled or no longer a Secondary, SecondaryCatalog or SecondaryForwarder zone
///         is deleted at start and by the first pass that sees it gone.
///     </para>
/// </summary>
/// <param name="scopeFactory">The factory used to create a scope per check.</param>
/// <param name="options">
///     The poll interval and retry delay, read through <see cref="IOptionsMonitor{TOptions}" /> on
///     every iteration; <see cref="IOptions{TOptions}.Value" /> would keep the startup value in
///     this singleton.
/// </param>
/// <param name="logger">The logger this service records refresh failures to.</param>
/// <param name="timeProvider">The clock every due time and refresh time comes from.</param>
/// <param name="expiryTracker">The tracker this service publishes each zone's expiry time to.</param>
public sealed class SecondaryZoneRefreshService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<SecondaryZoneRefreshOptions> options,
    ILogger<SecondaryZoneRefreshService> logger,
    TimeProvider timeProvider,
    SecondaryZoneExpiryTracker expiryTracker
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

    /// <summary>
    ///     This field holds the zones whose expiry has been logged and not yet recovered from, so
    ///     the Warning appears once per expiry and not on every failed pass.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, byte> _expiryWarned = new();

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

    /// <summary>
    ///     This method loads the stored refresh state into <see cref="SecondaryZoneExpiryTracker" />
    ///     before the loop starts. The host starts the hosted services in order and the listeners
    ///     after them, so no query is answered before an expired zone is known to be expired. A load
    ///     failure is logged and the service still starts: the zones then answer until a pass
    ///     publishes their expiry.
    /// </summary>
    public override async Task StartAsync(CancellationToken stoppingToken)
    {
        try
        {
            await LoadExpiryStateAsync(stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not load the secondary zone refresh state.");
        }

        await base.StartAsync(stoppingToken);
    }

    /// <summary>
    ///     This method gives every enabled Secondary, SecondaryCatalog and SecondaryForwarder zone
    ///     with a local SOA its expiry time (stored refresh time plus SOA Expire), writes a row with
    ///     the current time for a zone that has none, and deletes the rows of every other zone.
    /// </summary>
    internal async Task LoadExpiryStateAsync(CancellationToken stoppingToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IZoneRepository zoneRepository =
            scope.ServiceProvider.GetRequiredService<IZoneRepository>();
        IRecordRepository recordRepository =
            scope.ServiceProvider.GetRequiredService<IRecordRepository>();
        ISecondaryZoneRefreshStateRepository stateRepository =
            scope.ServiceProvider.GetRequiredService<ISecondaryZoneRefreshStateRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        IReadOnlyList<ZoneEntity> zones = await zoneRepository.ListAsync(
            zone =>
                !zone.IsDisabled
                && (
                    zone.Type == ZoneTypeEnum.Secondary
                    || zone.Type == ZoneTypeEnum.SecondaryCatalog
                    || zone.Type == ZoneTypeEnum.SecondaryForwarder
                ),
            stoppingToken
        );
        IReadOnlyList<SecondaryZoneRefreshStateEntity> states = await stateRepository.ListAsync(
            stoppingToken: stoppingToken
        );
        Dictionary<Guid, SecondaryZoneRefreshStateEntity> stateByZoneId = states.ToDictionary(
            state => state.ZoneId
        );
        HashSet<Guid> zoneIds = [.. zones.Select(zone => zone.Id)];
        DateTimeOffset now = timeProvider.GetUtcNow();

        // The table has no foreign key, so a row outlives its zone. Remove what no longer belongs.
        foreach (SecondaryZoneRefreshStateEntity state in states)
            if (!zoneIds.Contains(state.ZoneId))
                stateRepository.Remove(state);

        foreach (ZoneEntity zone in zones)
        {
            SoaRecordData soa = await ReadLocalSoaAsync(zone, recordRepository, stoppingToken);

            if (soa is null)
                continue;

            if (!stateByZoneId.TryGetValue(zone.Id, out SecondaryZoneRefreshStateEntity state))
            {
                state = new SecondaryZoneRefreshStateEntity
                {
                    Id = Guid.NewGuid(),
                    ZoneId = zone.Id,
                    LastRefreshedAtUtc = now,
                    CreatedAtUtc = now,
                };
                await stateRepository.AddAsync(state, stoppingToken);
            }

            if (soa.ExpireSeconds == 0)
                logger.LogWarning(
                    "Secondary zone '{ZoneName}' has SOA Expire 0, so it never expires.",
                    zone.Name
                );

            expiryTracker.SetExpiresAt(zone.Id, ExpiresAt(state.LastRefreshedAtUtc, soa));

            if (expiryTracker.IsExpired(zone.Id))
                WarnExpiredOnce(zone, soa);
        }

        await unitOfWork.SaveChangesAsync(stoppingToken);
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
    ///     This method returns when the next check of a zone is due, or <see langword="null" />
    ///     when it has none. It lets tests observe the Refresh and Retry scheduling.
    /// </summary>
    /// <param name="zoneId">The identifier of the zone.</param>
    internal DateTimeOffset? GetNextCheckAtUtc(Guid zoneId) =>
        _nextCheckAtUtc.TryGetValue(zoneId, out DateTimeOffset nextCheckAtUtc)
            ? nextCheckAtUtc
            : null;

    /// <summary>
    ///     This method checks every secondary zone whose next-check time has come due, and first
    ///     forgets the zones that are gone.
    /// </summary>
    internal async Task CheckDueZonesAsync(CancellationToken stoppingToken)
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

        DateTimeOffset now = timeProvider.GetUtcNow();

        await ForgetGoneZonesAsync(secondaryZones, scope.ServiceProvider, stoppingToken);

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
    ///     This method drops the schedule, the expiry and the stored refresh time of every zone that
    ///     is no longer in <paramref name="currentZones" /> (deleted, disabled or of another type).
    ///     The table has no foreign key, so nothing else removes a row. The rows are read on every
    ///     pass: there is at most one per secondary zone, and it catches a zone deleted before it was
    ///     ever checked.
    /// </summary>
    private async Task ForgetGoneZonesAsync(
        IReadOnlyList<ZoneEntity> currentZones,
        IServiceProvider services,
        CancellationToken stoppingToken
    )
    {
        ISecondaryZoneRefreshStateRepository stateRepository =
            services.GetRequiredService<ISecondaryZoneRefreshStateRepository>();
        HashSet<Guid> currentIds = [.. currentZones.Select(zone => zone.Id)];

        // Filtered in memory: the table holds at most one small row per secondary zone.
        List<SecondaryZoneRefreshStateEntity> goneStates =
        [
            .. (await stateRepository.ListAsync(stoppingToken: stoppingToken)).Where(state =>
                !currentIds.Contains(state.ZoneId)
            ),
        ];

        foreach (Guid zoneId in _nextCheckAtUtc.Keys.Where(id => !currentIds.Contains(id)))
        {
            _nextCheckAtUtc.TryRemove(zoneId, out _);
            _expiryWarned.TryRemove(zoneId, out _);
            expiryTracker.Remove(zoneId);
        }

        if (goneStates.Count == 0)
            return;

        foreach (SecondaryZoneRefreshStateEntity state in goneStates)
        {
            _expiryWarned.TryRemove(state.ZoneId, out _);
            expiryTracker.Remove(state.ZoneId);
            stateRepository.Remove(state);
        }

        await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync(stoppingToken);
    }

    /// <summary>
    ///     This method decides whether a primary's serial calls for a transfer, using RFC 1982 §3.2
    ///     arithmetic so a primary whose serial has wrapped still counts as ahead.
    /// </summary>
    /// <param name="localSerial">The zone's serial here, or <see langword="null" /> when there is no copy.</param>
    /// <param name="primarySerial">The serial the primary reported.</param>
    /// <returns>
    ///     <see langword="true" /> when there is no local copy or the primary is ahead; equal serials
    ///     and the undefined pair (2^31 apart) are not.
    /// </returns>
    internal static bool IsPrimaryAhead(uint? localSerial, uint primarySerial) =>
        localSerial is not { } local || SoaSerialArithmetic.IsGreaterThan(primarySerial, local);

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
            await HandleAllPrimariesFailedAsync(
                zone,
                services.GetRequiredService<IRecordRepository>(),
                stoppingToken
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
                    "No usable SOA answer from primary {Address} for zone '{ZoneName}' (no reply, or the reply was rejected).",
                    primaryAddress,
                    zone.Name
                );
                continue;
            }

            if (!IsPrimaryAhead(localSerial, primarySerial.Value))
            {
                await RecordRefreshSuccessAsync(zone, services, stoppingToken);
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

            bool applied = await ApplyPullResultAsync(
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

            // A pull the service skipped (an unusable answer) confirms nothing, so it is neither a
            // success nor a reason to stop trying the other primaries.
            if (!applied)
                continue;

            await RecordRefreshSuccessAsync(zone, services, stoppingToken);
            await RescheduleFromLocalSoaAsync(zone, recordRepository, stoppingToken);
            return;
        }

        await HandleAllPrimariesFailedAsync(zone, recordRepository, stoppingToken);
    }

    /// <summary>
    ///     This method decodes a <see cref="ZoneTypeEnum.SecondaryForwarder" /> zone's stored apex TXT
    ///     records (<see cref="ForwarderAddressTxtCodec" />) into its
    ///     <see cref="ZoneEntity.ForwarderAddresses" />, in the order the encoded indexes give, since list
    ///     order is failover priority. It does nothing for other types, or when the list is unchanged,
    ///     so a poll with no change writes nothing.
    /// </summary>
    private async Task SyncForwarderAddressesFromRecordsAsync(
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
        zone.UpdatedAtUtc = timeProvider.GetUtcNow();

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
                new DnsZoneChangedEvent(timeProvider.GetUtcNow(), zone.Id, zone.Name)
            );
            return;
        }
    }

    /// <summary>
    ///     This method applies a successful pull: it bootstraps a new zone, replays an incremental
    ///     diff step by step, or reconciles a full record set with what is stored.
    /// </summary>
    /// <returns>
    ///     <see langword="false" /> when the answer was unusable and nothing was applied; otherwise
    ///     <see langword="true" />.
    /// </returns>
    private async Task<bool> ApplyPullResultAsync(
        ZoneEntity zone,
        uint? localSerial,
        ZoneTransferPullResult pullResult,
        IRecordRepository recordRepository,
        IZoneUpdateService zoneUpdateService,
        CancellationToken stoppingToken
    )
    {
        if (pullResult.IsAlreadyCurrent)
            return true;

        if (localSerial is null)
        {
            if (pullResult.IsIncremental)
            {
                logger.LogWarning(
                    "Primary answered an initial transfer for zone '{ZoneName}' with an unexpected incremental response; skipping this refresh.",
                    zone.Name
                );
                return false;
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
            return true;
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

        return true;
    }

    /// <summary>
    ///     This method reads the zone's current SOA serial, or <see langword="null" /> if there is
    ///     no local copy yet.
    /// </summary>
    private static async Task<uint?> ReadLocalSerialAsync(
        ZoneEntity zone,
        IRecordRepository recordRepository,
        CancellationToken stoppingToken
    ) => (await ReadLocalSoaAsync(zone, recordRepository, stoppingToken))?.Serial;

    /// <summary>
    ///     This method reads the zone's apex SOA data, or <see langword="null" /> if there is no
    ///     local copy yet.
    /// </summary>
    private static async Task<SoaRecordData> ReadLocalSoaAsync(
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
            ? soaData
            : null;
    }

    /// <summary>
    ///     This method computes when a zone expires: the last refresh plus the SOA Expire field
    ///     (RFC 1035 §3.3.13).
    /// </summary>
    /// <returns>
    ///     The expiry time, or <see langword="null" /> when there is no SOA or its Expire is 0
    ///     (never expires).
    /// </returns>
    private static DateTimeOffset? ExpiresAt(
        DateTimeOffset lastRefreshedAtUtc,
        SoaRecordData soa
    ) =>
        soa is { ExpireSeconds: > 0 }
            ? lastRefreshedAtUtc + TimeSpan.FromSeconds(soa.ExpireSeconds)
            : null;

    /// <summary>
    ///     This method records that a primary confirmed the zone: it stores the current time as the
    ///     zone's refresh time through the state repository and unit of work (not through
    ///     <see cref="IZoneUpdateService" />, so no zone-changed event), and publishes the new expiry
    ///     time computed from the zone's (possibly just replaced) SOA. The tracker is updated first,
    ///     so a failing write cannot leave a zone expired that a primary just confirmed.
    /// </summary>
    private async Task RecordRefreshSuccessAsync(
        ZoneEntity zone,
        IServiceProvider services,
        CancellationToken stoppingToken
    )
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        IRecordRepository recordRepository = services.GetRequiredService<IRecordRepository>();
        SoaRecordData soa = await ReadLocalSoaAsync(zone, recordRepository, stoppingToken);

        bool wasExpired = expiryTracker.IsExpired(zone.Id);
        expiryTracker.SetExpiresAt(zone.Id, ExpiresAt(now, soa));

        if (_expiryWarned.TryRemove(zone.Id, out _) || wasExpired)
            logger.LogInformation(
                "Secondary zone '{ZoneName}' was refreshed and answers again.",
                zone.Name
            );

        ISecondaryZoneRefreshStateRepository stateRepository =
            services.GetRequiredService<ISecondaryZoneRefreshStateRepository>();
        IUnitOfWork unitOfWork = services.GetRequiredService<IUnitOfWork>();
        SecondaryZoneRefreshStateEntity state = await stateRepository.FindByZoneIdAsync(
            zone.Id,
            stoppingToken
        );

        if (state is null)
            await stateRepository.AddAsync(
                new SecondaryZoneRefreshStateEntity
                {
                    Id = Guid.NewGuid(),
                    ZoneId = zone.Id,
                    LastRefreshedAtUtc = now,
                    CreatedAtUtc = now,
                },
                stoppingToken
            );
        else
        {
            state.LastRefreshedAtUtc = now;
            stateRepository.Update(state);
        }

        await unitOfWork.SaveChangesAsync(stoppingToken);
    }

    /// <summary>
    ///     This method schedules the next check after every primary failed (RFC 1035 §4.3.5: the
    ///     SOA Retry interval), and logs the expiry once if the Expire interval has passed. A zone
    ///     with no local SOA, so no Retry value, uses
    ///     <see cref="SecondaryZoneRefreshOptions.DefaultRetryInterval" />.
    /// </summary>
    private async Task HandleAllPrimariesFailedAsync(
        ZoneEntity zone,
        IRecordRepository recordRepository,
        CancellationToken stoppingToken
    )
    {
        SoaRecordData soa = await ReadLocalSoaAsync(zone, recordRepository, stoppingToken);

        TimeSpan retry = soa is { RetrySeconds: > 0 }
            ? TimeSpan.FromSeconds(soa.RetrySeconds)
            : options.CurrentValue.DefaultRetryInterval;

        _nextCheckAtUtc[zone.Id] = timeProvider.GetUtcNow() + retry;

        if (soa is not null && expiryTracker.IsExpired(zone.Id))
            WarnExpiredOnce(zone, soa);
    }

    /// <summary>
    ///     This method logs that a zone expired, once until a refresh recovers it.
    /// </summary>
    private void WarnExpiredOnce(ZoneEntity zone, SoaRecordData soa)
    {
        if (_expiryWarned.TryAdd(zone.Id, 0))
            logger.LogWarning(
                "Secondary zone '{ZoneName}' expired: no primary confirmed it for {Expire}; it answers SERVFAIL until a refresh succeeds.",
                zone.Name,
                TimeSpan.FromSeconds(soa.ExpireSeconds)
            );
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
        SoaRecordData soaData = await ReadLocalSoaAsync(zone, recordRepository, stoppingToken);

        TimeSpan interval = soaData is { RefreshSeconds: > 0 }
            ? TimeSpan.FromSeconds(soaData.RefreshSeconds)
            : options.CurrentValue.DefaultRetryInterval;

        _nextCheckAtUtc[zone.Id] = timeProvider.GetUtcNow() + interval;
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
