using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Dnssec;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Dnssec;

/// <summary>
///     This hosted service advances DNSSEC key rollovers on a schedule, following RFC 6781
///     timing, for every zone signed here (<see cref="SignedZoneCache.CachedZoneIds" />). An
///     operator starts a rollover with <c>Server.Dnssec.IDnssecKeyManagementService.StartRolloverAsync</c>;
///     this service does the rest. ZSKs and KSKs use different schemes:
///     <para>
///         <b>ZSK, Pre-Publish (RFC 6781 §4.1.1.1).</b> The new key sits in
///         <see cref="DnssecKeyLifecycleStateEnum.Published" /> (in the DNSKEY RRset, signing
///         nothing) for the DNSKEY TTL plus <see cref="DnssecKeyRolloverOptions.PropagationDelay" />,
///         so caches learn it. Then it becomes <see cref="DnssecKeyLifecycleStateEnum.Active" />
///         and the old ZSK becomes <see cref="DnssecKeyLifecycleStateEnum.Retired" />: still
///         published so cached signatures verify, but no longer signing. After the zone's largest
///         record TTL plus the propagation delay, the old key is
///         <see cref="DnssecKeyLifecycleStateEnum.Removed" />.
///     </para>
///     <para>
///         <b>KSK, Double-Signature (RFC 6781 §4.1.2).</b> The new key starts in
///         <see cref="DnssecKeyLifecycleStateEnum.DoubleSignature" />, signing the DNSKEY RRset
///         next to the current Active KSK. <c>DnssecKeyEntity.RetiredAtUtc</c> tells the two
///         DoubleSignature keys apart: it is null for the incoming key and set on the outgoing one
///         when it is demoted. (Checking for an Active sibling does not work, since both have
///         one.) The incoming key is promoted to Active after the DNSKEY TTL plus propagation
///         delay, and only once the parent can publish its DS:
///         <list type="bullet">
///             <item>If the zone that answers the child's DS query is a signed Primary or
///             CloudPrimary hosted here, at any distance above the child, at once; this service
///             then re-signs that parent, which regenerates its DS for the new Active KSK
///             (<see cref="ZoneSigningService.ComputeDelegationDsRecordsAsync" />).</item>
///             <item>Otherwise only after an operator confirms the parent's DS was updated
///             (<c>IDnssecKeyManagementService.ConfirmParentDsUpdatedAsync</c>). The server
///             cannot check an external registrar, and removing a KSK too early breaks
///             validation.</item>
///         </list>
///         On promotion the old KSK goes back to DoubleSignature and keeps signing the DNSKEY
///         RRset. It is removed after the DS TTL plus propagation delay: the parent's real DS TTL
///         for a parent hosted here (at least 3600 seconds, more when a stored DS record sets a
///         longer TTL; see <see cref="FindLocalParent" />), or
///         <see cref="DnssecKeyRolloverOptions.ExternalParentDsTtl" /> otherwise.
///     </para>
/// </summary>
/// <param name="scopeFactory">The factory used to create a scope per zone for the repositories.</param>
/// <param name="signedZoneCache">
///     The cache that lists the zones to check (<see cref="SignedZoneCache.CachedZoneIds" />)
///     and that is reloaded right after a key changes state, without waiting for the event bus.
/// </param>
/// <param name="options">
///     The rollover timing, read through <see cref="IOptionsMonitor{TOptions}.CurrentValue" /> on
///     every iteration so changes apply without a restart.
/// </param>
/// <param name="logger">The logger this service records a rollover transition, or a per-zone failure, to.</param>
/// <param name="zoneCache">
///     The cache of the zones hosted here, which <see cref="FindLocalParent" /> reads to find the
///     zone that answers a child's DS query.
/// </param>
public class DnssecKeyRolloverBackgroundService(
    IServiceScopeFactory scopeFactory,
    SignedZoneCache signedZoneCache,
    IOptionsMonitor<DnssecKeyRolloverOptions> options,
    ILogger<DnssecKeyRolloverBackgroundService> logger,
    AuthoritativeZoneCache zoneCache
) : BackgroundService
{
    /// <summary>
    ///     This field contains the TTL <see cref="ZoneSigningService.ComputeDelegationDsRecordsAsync" />
    ///     gives generated DS records, the floor of the DS TTL when the parent is hosted here
    ///     (<see cref="FindLocalParent" /> raises it to a longer stored DS TTL). The two values
    ///     must stay equal.
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
                    // One zone's bad state (a deleted key, say) must not stop the others.
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
                // Expected on shutdown; the loop condition ends the loop.
            }
        }
    }

    /// <summary>
    ///     This method applies every rollover step that is due for one zone. If anything changed,
    ///     it saves the keys and re-signs the zone, and the parent zone too when a KSK was promoted
    ///     under a parent hosted here.
    /// </summary>
    private async Task ProcessZoneAsync(Guid zoneId, CancellationToken stoppingToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        // A manual rollover start must not interleave with this read-modify-save: it would see
        // keys this pass is about to change, or this pass would overwrite its new key's state.
        // The gate is released before the zone reload, which can take long.
        DnssecZoneKeyGate zoneKeyGate =
            scope.ServiceProvider.GetRequiredService<DnssecZoneKeyGate>();
        Guid? parentToReload;
        bool changed;

        using (IDisposable zoneLease = await zoneKeyGate.EnterAsync(zoneId, stoppingToken))
        {
            (changed, parentToReload) = await AdvanceKeysAsync(scope, zoneId, stoppingToken);
        }

        if (!changed)
            return;

        // Re-sign now rather than waiting for the event bus: the DNSKEY RRset and the signing
        // keys just changed.
        await signedZoneCache.ReloadZoneAsync(zoneId, stoppingToken);

        // Re-sign a parent hosted here too, since its DS for the new KSK is only generated when
        // the parent is signed.
        if (parentToReload.HasValue)
            await signedZoneCache.ReloadZoneAsync(parentToReload.Value, stoppingToken);
    }

    /// <summary>
    ///     This method reads one zone's keys, applies every due rollover step and saves. It runs
    ///     under the zone's <see cref="DnssecZoneKeyGate" /> lease.
    /// </summary>
    /// <returns>
    ///     Whether any key changed, and the parent zone to re-sign when a KSK was promoted under
    ///     a parent hosted here.
    /// </returns>
    private async Task<(bool Changed, Guid? ParentZoneToReload)> AdvanceKeysAsync(
        AsyncServiceScope scope,
        Guid zoneId,
        CancellationToken stoppingToken
    )
    {
        IZoneRepository zoneRepository =
            scope.ServiceProvider.GetRequiredService<IZoneRepository>();
        IRecordRepository recordRepository =
            scope.ServiceProvider.GetRequiredService<IRecordRepository>();
        IDnssecKeyRepository keyRepository =
            scope.ServiceProvider.GetRequiredService<IDnssecKeyRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        ZoneEntity zone = await zoneRepository.GetByIdAsync(zoneId, stoppingToken);

        // The zone may have been deleted or unsigned since it was cached; SignedZoneCache drops
        // it on its next reload.
        if (zone is not { IsDnssecSigned: true })
            return (false, null);

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
        (bool IsLocalParent, Guid? ParentZoneId, TimeSpan DsTtl)? localParent = null;

        foreach (
            DnssecKeyEntity ksk in keys.Where(key =>
                key.Role == DnssecKeyRoleEnum.Ksk
                && key.LifecycleState == DnssecKeyLifecycleStateEnum.DoubleSignature
            )
        )
        {
            localParent ??= FindLocalParent(zone);

            // RetiredAtUtc is null only for the incoming key; the outgoing key got it when it was
            // demoted. See the class summary.
            if (!ksk.RetiredAtUtc.HasValue)
            {
                // Incoming KSK: promote it and demote the current one once it is safe.
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
                    promotedKskLocalParentZoneId = localParent.Value.ParentZoneId;
            }
            else
            {
                // Outgoing KSK: remove it once the old DS has expired from caches.
                TimeSpan dsTtl = localParent.Value.IsLocalParent
                    ? localParent.Value.DsTtl
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
            return (false, null);

        await unitOfWork.SaveChangesAsync(stoppingToken);

        return (true, promotedKskLocalParentZoneId);
    }

    /// <summary>
    ///     This method returns the zone's DNSKEY TTL, computed as
    ///     <see cref="ZoneSigningService.SignZoneAsync" /> does: the SOA record's TTL, or 3600
    ///     seconds without an SOA. The two must stay in step.
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
    ///     This method returns the largest TTL of any enabled record in the zone (the RFC 6781
    ///     "maximum zone TTL"), which bounds how long an old signature can stay in a cache. A zone
    ///     with no records uses 3600 seconds.
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
    ///     This method finds the local parent of <paramref name="childZone" />: the zone this
    ///     server answers the child's DS query from
    ///     (<see cref="AuthoritativeZoneCache.FindZone" /> with type DS), at any label distance
    ///     above the child. It counts only when that zone is a signed Primary or CloudPrimary and
    ///     its topmost zone cut at the child's name is the child itself, so a child hidden below a
    ///     higher cut is external. Only then is the DS generated automatically
    ///     (<see cref="ZoneSigningService.ComputeDelegationDsRecordsAsync" />). It reads the
    ///     in-memory cache, which every zone write reloads, so it does not query the database.
    /// </summary>
    /// <param name="childZone">The zone whose KSK is rolling.</param>
    /// <returns>
    ///     Whether the parent is hosted here, the parent's zone identifier (null when it is not),
    ///     and the DS TTL to wait for: at least <see cref="LocalDelegationDsTtl" />, or the
    ///     longest TTL of a DS record stored at the child's name in the parent when that is
    ///     longer, because the signer gives the DS RRset the stored TTL (RFC 4034 §3 needs the
    ///     RRSIG to carry the same TTL). For an external parent the TTL is the 3600-second
    ///     floor and the caller does not use it.
    /// </returns>
    internal (bool IsLocalParent, Guid? ParentZoneId, TimeSpan DsTtl) FindLocalParent(
        ZoneEntity childZone
    )
    {
        DnsName childName = DnsName.Parse(childZone.Name);
        CachedZone parent = zoneCache.FindZone(childName, DnsResourceRecordTypeEnum.DS);

        if (
            parent is null
            || parent.Zone.Id == childZone.Id
            || !parent.Zone.IsDnssecSigned
            || parent.Zone.Type is not (ZoneTypeEnum.Primary or ZoneTypeEnum.CloudPrimary)
        )
            return (false, null, LocalDelegationDsTtl);

        // Names below a cut are not authoritative data of the zone above it (RFC 1034 §4.2.1),
        // so a cut above the child leaves the parent without a delegation for the child itself
        // and publishes no DS for it.
        if (parent.FindEnclosingDelegation(childName)?.DelegationPoint != childName)
            return (false, null, LocalDelegationDsTtl);

        uint storedTtlSeconds = parent
            .GetRecords(childName, DnsResourceRecordTypeEnum.DS)
            .Select(record => record.TimeToLiveSeconds)
            .DefaultIfEmpty(0u)
            .Max();

        return (
            true,
            parent.Zone.Id,
            TimeSpan.FromSeconds(Math.Max(LocalDelegationDsTtl.TotalSeconds, storedTtlSeconds))
        );
    }
}
