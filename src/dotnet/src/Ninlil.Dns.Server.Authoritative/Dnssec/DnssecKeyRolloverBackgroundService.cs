using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Dnssec;

/// <summary>
///     This class walks every locally signed zone's own DNSSEC keys on a schedule, advancing any
///     key mid-rollover through RFC 6781's own real timing rules -- the automated half of the
///     rollover choreography <c>Server.Dnssec.IDnssecKeyManagementService.StartRolloverAsync</c>
///     only ever starts. Two independent schemes run side by side, one per role, matched to the
///     scheme RFC 6781 itself recommends for each:
///     <para>
///         <b>Zone-Signing Key, Pre-Publish (RFC 6781 §4.1.1.1)</b>: an incoming key sits in
///         <see cref="DnssecKeyLifecycleStateEnum.Published" /> -- present in the DNSKEY RRset,
///         signing nothing -- for at least the zone's own DNSKEY-RRset TTL plus
///         <see cref="DnssecKeyRolloverOptions.PropagationDelay" />, the minimum time RFC 6781
///         itself gives for that RRset to actually reach every cache that might depend on it. Once
///         that wait elapses, it is promoted to <see cref="DnssecKeyLifecycleStateEnum.Active" />
///         and immediately becomes the zone's own sole ordinary-data signer; whichever key it
///         replaced steps down to <see cref="DnssecKeyLifecycleStateEnum.Retired" /> in the same
///         moment -- still published, so an RRSIG it already produced under the previous key set
///         stays verifiable, but signing nothing further. A <see cref="DnssecKeyLifecycleStateEnum.Retired" />
///         key is finally withdrawn (<see cref="DnssecKeyLifecycleStateEnum.Removed" />) once the
///         zone's own maximum record TTL, plus the same propagation delay, has elapsed since it
///         stepped down -- RFC 6781's own bound for how long data signed under the outgoing key
///         could still be sitting in a real resolver's cache.
///     </para>
///     <para>
///         <b>Key-Signing Key, Double-Signature (RFC 6781 §4.1.2, Figure 4)</b>: an incoming key
///         starts directly in <see cref="DnssecKeyLifecycleStateEnum.DoubleSignature" />, already
///         co-signing the DNSKEY RRset alongside the zone's own current
///         <see cref="DnssecKeyLifecycleStateEnum.Active" /> KSK -- there is no Published-only
///         stage for a KSK, since RFC 6781's own Figure 4 shows both keys' RRSIGs present from the
///         very first "new DNSKEY" stage onward. This service tells "the incoming key, not yet
///         canonical" and "the outgoing key, no longer canonical" apart by whether
///         <c>DnssecKeyEntity.RetiredAtUtc</c> is set: the incoming key has never previously been
///         <see cref="DnssecKeyLifecycleStateEnum.Active" />, so it is always <see langword="null" />;
///         the outgoing key was demoted straight back into
///         <see cref="DnssecKeyLifecycleStateEnum.DoubleSignature" /> from
///         <see cref="DnssecKeyLifecycleStateEnum.Active" />, which sets it, the same instant a ZSK's
///         own outgoing key's does entering <see cref="DnssecKeyLifecycleStateEnum.Retired" />.
///         Deliberately not "does this key currently have an Active sibling of the same role" --
///         both the incoming key (whose sibling is the still-Active predecessor) and the
///         just-demoted outgoing key (whose sibling is its own freshly promoted replacement) equally
///         have one at once, so that check alone cannot tell them apart. The incoming key is
///         promoted to
///         <see cref="DnssecKeyLifecycleStateEnum.Active" /> -- becoming the one KSK a parent
///         zone's own DS record should now name -- once the same DNSKEY-RRset-TTL-plus-
///         propagation-delay wait as a ZSK's own pre-roll has elapsed <i>and</i> this zone's own
///         parent has actually had a chance to update that DS record: automatically, with no
///         further wait or operator step at all, when the parent is itself a zone this same
///         server hosts and signs (<see cref="ZoneSigningService.ComputeDelegationDsRecordsAsync" />
///         already regenerates that parent's own DS content for whichever KSK is currently
///         <see cref="DnssecKeyLifecycleStateEnum.Active" />, the very next time the parent is
///         re-signed -- which this service explicitly triggers, immediately, rather than waiting
///         for that parent's own unrelated schedule); or, when the parent is any real,
///         externally-hosted zone this server has no way to observe or control, only once an
///         operator has explicitly confirmed the swap really happened
///         (<c>Server.Dnssec.IDnssecKeyManagementService.ConfirmParentDsUpdatedAsync</c> --
///         this project has no live mechanism of its own to check a real registrar's own DS
///         record, and a real KSK removal is destructive enough that guessing wrong is far worse
///         than simply waiting for a human to say so). The moment the incoming key is promoted,
///         its outgoing predecessor is demoted straight back to
///         <see cref="DnssecKeyLifecycleStateEnum.DoubleSignature" /> -- it keeps co-signing the
///         DNSKEY RRset, exactly as Figure 4 shows through its own "DS change" stage, rather than
///         stopping immediately the way a ZSK's outgoing key does -- and is finally withdrawn once
///         the relevant DS record's own TTL, plus the configured propagation delay, has elapsed
///         since that demotion: the real, locally known 3600-second value
///         (<see cref="ZoneSigningService.ComputeDelegationDsRecordsAsync" />'s own hardcoded DS
///         TTL) for a locally hosted parent, or <see cref="DnssecKeyRolloverOptions.ExternalParentDsTtl" />'s
///         own configured assumption otherwise.
///     </para>
/// </summary>
/// <param name="scopeFactory">
///     The factory used to reach the repositories this service reads and writes through, in a
///     scope of its own per zone, the same pattern <see cref="SignedZoneCache" /> and
///     <see cref="DnssecResigningBackgroundService" /> already establish.
/// </param>
/// <param name="signedZoneCache">
///     The cache this service both reads (<see cref="SignedZoneCache.CachedZoneIds" />, the exact
///     set of locally signed zones this service ever needs to check) and writes back to
///     (<see cref="SignedZoneCache.ReloadZoneAsync" />, called immediately after any real
///     transition this service makes, the same "do not wait on the event bus alone" reasoning
///     <see cref="SignedZoneCache.ResignForScheduledExpiryAsync" />'s own doc comment already
///     gives for a scheduled resign).
/// </param>
/// <param name="options">
///     This service's own timing configuration, read fresh
///     (<see cref="IOptionsMonitor{TOptions}.CurrentValue" />) on every loop iteration rather than
///     snapshotted once -- the same <see cref="IOptions{TOptions}" />-vs-<see cref="IOptionsMonitor{TOptions}" />
///     staleness trap <see cref="DnssecResigningBackgroundService" />'s own constructor doc
///     comment already documents for a singleton hosted service.
/// </param>
/// <param name="logger">The logger this service records a rollover transition, or a per-zone failure, to.</param>
public class DnssecKeyRolloverBackgroundService(
    IServiceScopeFactory scopeFactory,
    SignedZoneCache signedZoneCache,
    IOptionsMonitor<DnssecKeyRolloverOptions> options,
    ILogger<DnssecKeyRolloverBackgroundService> logger
) : BackgroundService
{
    /// <summary>
    ///     This constant contains the real, locally known Time To Live this project's own DS
    ///     automation always gives a delegation's DS record
    ///     (<see cref="ZoneSigningService.ComputeDelegationDsRecordsAsync" />), reused here so a
    ///     KSK rollover's own final safe-removal wait for a locally hosted parent measures against
    ///     the exact value that parent's own signing pass actually publishes, rather than a second,
    ///     independently maintained copy of the same number.
    /// </summary>
    private static readonly TimeSpan LocalDelegationDsTtl = TimeSpan.FromSeconds(3600);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (Guid zoneId in signedZoneCache.CachedZoneIds)
            {
                stoppingToken.ThrowIfCancellationRequested();

                try
                {
                    await ProcessZoneAsync(zoneId, stoppingToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // A single zone's own bad state (a key deleted out from under it, for example)
                    // must not stop every other zone's own rollover from being checked.
                    logger.LogError(
                        exception,
                        "Failed to check zone '{ZoneId}' for a due DNSSEC key rollover transition.",
                        zoneId
                    );
                }
            }

            try
            {
                await Task.Delay(options.CurrentValue.PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown -- the loop condition above ends the loop cleanly.
            }
        }
    }

    /// <summary>
    ///     This method checks a single zone's own key set for a due rollover transition, applies
    ///     every transition that is due in one pass, and -- only if anything actually changed --
    ///     persists the change and immediately re-signs both this zone and (for a promoted KSK with
    ///     a locally hosted parent) that parent too.
    /// </summary>
    private async Task ProcessZoneAsync(Guid zoneId, CancellationToken stoppingToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IZoneRepository zoneRepository =
            scope.ServiceProvider.GetRequiredService<IZoneRepository>();
        IRecordRepository recordRepository =
            scope.ServiceProvider.GetRequiredService<IRecordRepository>();
        IDnssecKeyRepository keyRepository =
            scope.ServiceProvider.GetRequiredService<IDnssecKeyRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        ZoneEntity zone = await zoneRepository.GetByIdAsync(zoneId, stoppingToken);

        // The zone may have been deleted, disabled, or unmarked-signed since SignedZoneCache last
        // observed it -- nothing to walk in that case; SignedZoneCache's own reload path already
        // handles dropping it from CachedZoneIds.
        if (zone is not { IsDnssecSigned: true })
            return;

        List<DnssecKeyEntity> keys =
        [
            .. await keyRepository.ListByZoneIdAsync(zoneId, stoppingToken),
        ];
        DateTimeOffset now = DateTimeOffset.UtcNow;
        TimeSpan propagationDelay = options.CurrentValue.PropagationDelay;

        bool changed = false;
        Guid? promotedKskLocalParentZoneId = null;

        // -- Zone-Signing Key, Pre-Publish (RFC 6781 §4.1.1.1) --
        foreach (
            DnssecKeyEntity incoming in keys.Where(key =>
                key.Role == DnssecKeyRoleEnum.Zsk
                && key.LifecycleState == DnssecKeyLifecycleStateEnum.Published
            )
        )
        {
            TimeSpan wait =
                await DnsKeyRrsetTtlAsync(recordRepository, zoneId, stoppingToken)
                + propagationDelay;

            if (now < incoming.PublishedAtUtc + wait)
                continue;

            incoming.LifecycleState = DnssecKeyLifecycleStateEnum.Active;
            incoming.UpdatedAtUtc = now;
            keyRepository.Update(incoming);
            changed = true;

            foreach (
                DnssecKeyEntity outgoing in keys.Where(key =>
                    key.Id != incoming.Id
                    && key.Role == DnssecKeyRoleEnum.Zsk
                    && key.LifecycleState == DnssecKeyLifecycleStateEnum.Active
                )
            )
            {
                outgoing.LifecycleState = DnssecKeyLifecycleStateEnum.Retired;
                outgoing.RetiredAtUtc = now;
                outgoing.UpdatedAtUtc = now;
                keyRepository.Update(outgoing);
            }
        }

        // -- Zone-Signing Key final withdrawal --
        foreach (
            DnssecKeyEntity retired in keys.Where(key =>
                key.Role == DnssecKeyRoleEnum.Zsk
                && key.LifecycleState == DnssecKeyLifecycleStateEnum.Retired
                && key.RetiredAtUtc.HasValue
            )
        )
        {
            TimeSpan wait =
                await MaximumRecordTtlAsync(recordRepository, zoneId, stoppingToken)
                + propagationDelay;

            if (now < retired.RetiredAtUtc!.Value + wait)
                continue;

            retired.LifecycleState = DnssecKeyLifecycleStateEnum.Removed;
            retired.UpdatedAtUtc = now;
            keyRepository.Update(retired);
            changed = true;
        }

        // -- Key-Signing Key, Double-Signature (RFC 6781 §4.1.2) --
        (Guid Id, bool IsLocalParent, Guid? LocalParentZoneId)? localParent = null;

        foreach (
            DnssecKeyEntity ksk in keys.Where(key =>
                key.Role == DnssecKeyRoleEnum.Ksk
                && key.LifecycleState == DnssecKeyLifecycleStateEnum.DoubleSignature
            )
        )
        {
            localParent ??= await FindLocalSignedParentZoneAsync(
                zoneRepository,
                recordRepository,
                zone,
                stoppingToken
            );

            // A DoubleSignature KSK's own RetiredAtUtc is what actually tells "the rollover's
            // still-incoming key, never previously canonical" apart from "the rollover's already-
            // displaced outgoing key, demoted straight back here from Active" -- both can equally
            // have an Active sibling at once (a freshly demoted outgoing key's own sibling is
            // exactly the incoming key that just replaced it), so checking for one is not a safe
            // way to tell them apart; only the incoming key has never had RetiredAtUtc set at all.
            if (!ksk.RetiredAtUtc.HasValue)
            {
                // The rollover's own incoming KSK, not yet canonical -- decide whether it is now
                // safe to promote it and demote its predecessor.
                DnssecKeyEntity activeSibling = keys.FirstOrDefault(key =>
                    key.Id != ksk.Id
                    && key.Role == DnssecKeyRoleEnum.Ksk
                    && key.LifecycleState == DnssecKeyLifecycleStateEnum.Active
                );

                TimeSpan wait1 =
                    await DnsKeyRrsetTtlAsync(recordRepository, zoneId, stoppingToken)
                    + propagationDelay;

                if (now < ksk.PublishedAtUtc + wait1)
                    continue;

                bool readyForCutover =
                    localParent.Value.IsLocalParent || ksk.ParentDsConfirmedAtUtc.HasValue;

                if (!readyForCutover)
                    continue;

                ksk.LifecycleState = DnssecKeyLifecycleStateEnum.Active;
                ksk.UpdatedAtUtc = now;
                keyRepository.Update(ksk);

                if (activeSibling is not null)
                {
                    activeSibling.LifecycleState = DnssecKeyLifecycleStateEnum.DoubleSignature;
                    activeSibling.RetiredAtUtc = now;
                    activeSibling.UpdatedAtUtc = now;
                    keyRepository.Update(activeSibling);
                }

                changed = true;

                if (localParent.Value.IsLocalParent)
                    promotedKskLocalParentZoneId = localParent.Value.LocalParentZoneId;
            }
            else
            {
                // The rollover's own outgoing KSK, already displaced -- decide whether it is now
                // safe to withdraw it entirely.
                TimeSpan dsTtl = localParent.Value.IsLocalParent
                    ? LocalDelegationDsTtl
                    : options.CurrentValue.ExternalParentDsTtl;
                TimeSpan wait2 = dsTtl + propagationDelay;

                if (now < ksk.RetiredAtUtc.Value + wait2)
                    continue;

                ksk.LifecycleState = DnssecKeyLifecycleStateEnum.Removed;
                ksk.UpdatedAtUtc = now;
                keyRepository.Update(ksk);
                changed = true;
            }
        }

        if (!changed)
            return;

        await unitOfWork.SaveChangesAsync(stoppingToken);

        // Re-sign explicitly and immediately, the same "do not wait on the asynchronously
        // delivered event bus alone" reasoning SignedZoneCache.ResignForScheduledExpiryAsync's own
        // doc comment already gives -- a key-state transition changes what this zone's own DNSKEY
        // RRset actually contains and which key signs what, exactly like an ordinary record change
        // would.
        await signedZoneCache.ReloadZoneAsync(zoneId, stoppingToken);

        // A promoted KSK's own delegating parent zone (when locally hosted) must also be re-signed
        // right away, so its own DS record actually catches up to which KSK is now canonical --
        // ComputeDelegationDsRecordsAsync only ever regenerates a parent's own DS content as part
        // of that parent's own next sign, which nothing else here would otherwise trigger promptly.
        if (promotedKskLocalParentZoneId.HasValue)
            await signedZoneCache.ReloadZoneAsync(
                promotedKskLocalParentZoneId.Value,
                stoppingToken
            );
    }

    /// <summary>
    ///     This method reads a zone's own DNSKEY RRset TTL exactly the way
    ///     <see cref="ZoneSigningService.SignZoneAsync" /> itself computes it (its own SOA record's
    ///     TTL, or a 3600-second fallback when the zone has no SOA on record yet) -- the real value
    ///     a rollover's own pre-roll wait must actually be measured against, not a second,
    ///     independently maintained assumption of what that TTL probably is.
    /// </summary>
    private static async Task<TimeSpan> DnsKeyRrsetTtlAsync(
        IRecordRepository recordRepository,
        Guid zoneId,
        CancellationToken stoppingToken
    )
    {
        IReadOnlyList<RecordEntity> records = await recordRepository.ListByZoneIdAsync(
            zoneId,
            stoppingToken
        );
        uint ttlSeconds = records
            .Where(record => record.Type == DnsResourceRecordTypeEnum.Soa)
            .Select(record => record.TimeToLiveSeconds)
            .DefaultIfEmpty(3600u)
            .First();

        return TimeSpan.FromSeconds(ttlSeconds);
    }

    /// <summary>
    ///     This method computes the highest TTL any enabled record in a zone currently carries --
    ///     RFC 6781's own "Maximum Zone TTL" term, the real bound on how long any signature this
    ///     zone has ever produced could still be sitting, unexpired, in some resolver's cache. A
    ///     zone with no records at all falls back to a conservative 3600-second assumption, the
    ///     same fallback <see cref="DnsKeyRrsetTtlAsync" /> uses for its own missing-SOA case.
    /// </summary>
    private static async Task<TimeSpan> MaximumRecordTtlAsync(
        IRecordRepository recordRepository,
        Guid zoneId,
        CancellationToken stoppingToken
    )
    {
        IReadOnlyList<RecordEntity> records = await recordRepository.ListByZoneIdAsync(
            zoneId,
            stoppingToken
        );
        uint maxTtlSeconds = records
            .Where(record => !record.IsDisabled)
            .Select(record => record.TimeToLiveSeconds)
            .DefaultIfEmpty(3600u)
            .Max();

        return TimeSpan.FromSeconds(maxTtlSeconds);
    }

    /// <summary>
    ///     This method looks for a real, locally hosted, signed zone that actually delegates to
    ///     <paramref name="childZone" /> -- the one case
    ///     <see cref="ZoneSigningService.ComputeDelegationDsRecordsAsync" /> already generates a
    ///     real DS record for automatically, with no operator involvement, every time that parent
    ///     is (re-)signed. Checks both that a zone by the expected parent name exists and is
    ///     itself signed, <i>and</i> that it actually carries an NS record naming
    ///     <paramref name="childZone" /> -- a same-named zone that happens to exist locally but
    ///     does not really delegate to this one (an unrelated, coincidentally-parent-shaped zone)
    ///     must never be mistaken for this delegation's own real parent, since doing so would skip
    ///     the one safety gate (an explicit operator confirmation) a genuinely external delegation
    ///     actually needs.
    /// </summary>
    private static async Task<(
        Guid Id,
        bool IsLocalParent,
        Guid? LocalParentZoneId
    )> FindLocalSignedParentZoneAsync(
        IZoneRepository zoneRepository,
        IRecordRepository recordRepository,
        ZoneEntity childZone,
        CancellationToken stoppingToken
    )
    {
        DnsName childName = DnsName.Parse(childZone.Name);

        if (childName.Labels.Count <= 1)
            return (childZone.Id, false, null);

        DnsName parentName = new([.. childName.Labels.Skip(1)]);

        ZoneEntity parent = (
            await zoneRepository.ListAsync(
                zone =>
                    zone.Name == parentName.ToString().TrimEnd('.')
                    && zone.IsDnssecSigned
                    && (
                        zone.Type == ZoneTypeEnum.Primary || zone.Type == ZoneTypeEnum.CloudPrimary
                    ),
                stoppingToken
            )
        ).FirstOrDefault();

        if (parent is null)
            return (childZone.Id, false, null);

        IReadOnlyList<RecordEntity> parentRecords = await recordRepository.ListByZoneIdAsync(
            parent.Id,
            stoppingToken
        );
        bool actuallyDelegates = parentRecords.Any(record =>
            !record.IsDisabled
            && record.Type == DnsResourceRecordTypeEnum.NS
            && DnsName.Parse(record.Name) == childName
        );

        return actuallyDelegates ? (childZone.Id, true, parent.Id) : (childZone.Id, false, null);
    }
}
