using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Authoritative.Journal;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Dnssec;

/// <summary>
///     This class holds every signed zone's current <see cref="SignedZoneData" /> in memory --
///     the DNSSEC counterpart to <see cref="Cache.AuthoritativeZoneCache" />, computed once by
///     <see cref="IZoneSigningService" /> and served from here thereafter, matching this project's
///     deliberate stance against ever signing live, per query. Kept as its own cache, entirely separate
///     from <see cref="Cache.AuthoritativeZoneCache" />'s ordinary zone data, since a signed
///     RRset's RRSIG/NSEC/NSEC3/DNSKEY content is generated, not operator-edited, and re-signing
///     it should never be conflated with an ordinary record change going through
///     <see cref="Journal.IZoneUpdateService" />.
/// </summary>
/// <param name="scopeFactory">
///     The factory used to reach the repositories and signing service this cache loads through,
///     in a scope of its own so it never holds a database context open longer than a single sign.
/// </param>
/// <param name="logger">The logger this cache records a single zone's signing failure to.</param>
/// <param name="eventPublisher">
///     The publisher this cache raises <see cref="DnssecZoneResigningCompletedEvent" /> through
///     after every successful (re-)sign -- optional, defaulting to <see langword="null" />, the
///     same pattern every other optional cross-cutting dependency in this project's constructors
///     already follows (see <c>DnsQueryProcessingPipeline</c>'s own <c>filterEngine</c>/
///     <c>rateLimiter</c> parameters), so every pre-existing caller/test that does not supply one
///     keeps compiling and behaving unchanged.
/// </param>
public sealed class SignedZoneCache(
    IServiceScopeFactory scopeFactory,
    ILogger<SignedZoneCache> logger,
    IDnsServerEventPublisher eventPublisher = null
) : IHostedService
{
    /// <summary>
    ///     This property contains every currently cached, locally computed signed zone, keyed by
    ///     its identifier -- a Primary/CloudPrimary zone this instance itself signs from its own
    ///     key material (see <see cref="TrySignAndStoreAsync" />). This is the one set
    ///     <see cref="CachedZoneIds" /> exposes, and therefore the only one
    ///     <see cref="DnssecResigningBackgroundService" />'s own proactive re-sign loop ever
    ///     walks -- see <see cref="_transferredSignedZonesByZoneId" />'s own doc comment for why a
    ///     secondary zone's held content is kept in a dictionary of its own instead.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, SignedZoneData> _signedZonesByZoneId = new();

    /// <summary>
    ///     This property contains every currently cached signed zone whose own RRSIG/NSEC/NSEC3/
    ///     NSEC3PARAM/DNSKEY/DS content was never computed by this instance at all, but arrived
    ///     pre-signed via a real zone transfer (Secondary/SecondaryCatalog/CloudSecondary) and is
    ///     held here exactly as received -- see <see cref="TransferredSignedZoneDataBuilder" /> for
    ///     what "held" actually means. Deliberately a second dictionary rather than folded into
    ///     <see cref="_signedZonesByZoneId" />: <see cref="CachedZoneIds" /> (and, through it,
    ///     <see cref="DnssecResigningBackgroundService" />'s own proactive resign-then-bump-serial
    ///     loop) must never walk a secondary zone at all -- this instance holds no private key
    ///     material to re-sign it with, and bumping its own SOA serial would be an unauthorized
    ///     write to a zone this server does not own the master copy of.
    ///     <see cref="GetSignedData" /> is the one place both dictionaries are actually merged, so
    ///     <see cref="Resolution.AuthoritativeQueryResolver" />/<see cref="Transfer.ZoneTransferService" />
    ///     need not know or care which one actually holds a given zone's entry.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, SignedZoneData> _transferredSignedZonesByZoneId =
        new();

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IZoneRepository zoneRepository =
            scope.ServiceProvider.GetRequiredService<IZoneRepository>();

        // Local signing exists to produce RRSIGs for content this instance itself authors and
        // serves. A classic Primary zone is the obvious case; a CloudPrimary zone is the same
        // case in substance -- this instance is still the authoritative source pushed out to the
        // cloud connector, and nothing signs it upstream first. A Secondary/CloudSecondary zone
        // is deliberately excluded here: its signed content (RRSIGs included) always arrives
        // pre-signed via NOTIFY/AXFR from whatever is actually authoritative, so this cache
        // signing it too would both be redundant and risk producing signatures that disagree
        // with the real primary's own -- see the loop immediately below for how a zone like that
        // is actually handled instead.
        IReadOnlyList<ZoneEntity> signedZones = await zoneRepository.ListAsync(
            zone =>
                !zone.IsDisabled
                && (zone.Type == ZoneTypeEnum.Primary || zone.Type == ZoneTypeEnum.CloudPrimary)
                && zone.IsDnssecSigned,
            cancellationToken
        );

        // One misconfigured zone (marked signed but with no active key yet, for example) must
        // not prevent every other signed zone from loading at all -- the same reasoning
        // DnssecResigningBackgroundService's own per-zone try/catch already follows.
        foreach (ZoneEntity zone in signedZones)
            await TrySignAndStoreAsync(zone.Id, zone.Name, cancellationToken);

        // A signed secondary zone already sitting on disk at boot -- its own AXFR/IXFR-transferred
        // RRSIG/NSEC/NSEC3/NSEC3PARAM/DNSKEY content already committed by an earlier process run --
        // must have that content loaded into this cache too, not only ever picked up the next time
        // a live transfer happens to complete. Left undone, a validating client querying it would
        // go right back to receiving no RRSIG at all for the entire interval between this restart
        // and whenever that zone's next scheduled refresh happens to land.
        //
        // The zone-type check here is written out as a literal three-way comparison rather than a
        // call to IsTransferredSignedZoneType, deliberately -- EF Core's LINQ provider cannot
        // translate an arbitrary static C# method call into SQL, exactly the same real,
        // one-time-discovered trap AuthoritativeZoneCache.StartAsync's own doc comment already
        // records; a version calling that helper directly inside this predicate throws
        // InvalidOperationException at query time, not compile time.
        IReadOnlyList<ZoneEntity> transferredSignedZones = await zoneRepository.ListAsync(
            zone =>
                !zone.IsDisabled
                && zone.IsDnssecSigned
                && (
                    zone.Type == ZoneTypeEnum.Secondary
                    || zone.Type == ZoneTypeEnum.SecondaryCatalog
                    || zone.Type == ZoneTypeEnum.CloudSecondary
                ),
            cancellationToken
        );

        foreach (ZoneEntity zone in transferredSignedZones)
            await TryLoadTransferredSignedDataAsync(zone.Id, zone.Name, cancellationToken);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    ///     This method looks up a zone's currently cached signed content -- whichever of this
    ///     cache's two dictionaries actually holds it, a locally-signed Primary/CloudPrimary zone
    ///     or a secondary-flavor zone's own held, transferred content; the caller (
    ///     <see cref="Resolution.AuthoritativeQueryResolver" />/<see cref="Transfer.ZoneTransferService" />)
    ///     never needs to know or care which.
    /// </summary>
    /// <param name="zoneId">The identifier of the zone to look up.</param>
    /// <returns>
    ///     The zone's signed content, or <see langword="null" /> if the zone is not signed, or
    ///     has not been signed (or, for a secondary-flavor zone, transferred) yet.
    /// </returns>
    public SignedZoneData GetSignedData(Guid zoneId) =>
        _signedZonesByZoneId.TryGetValue(zoneId, out SignedZoneData signedData)
            ? signedData
            : _transferredSignedZonesByZoneId.GetValueOrDefault(zoneId);

    /// <summary>
    ///     This property contains the identifiers of every zone currently held in this cache,
    ///     used by <see cref="DnssecResigningBackgroundService" /> to know which zones to check
    ///     for an approaching signature expiration.
    /// </summary>
    public IReadOnlyCollection<Guid> CachedZoneIds => [.. _signedZonesByZoneId.Keys];

    /// <summary>
    ///     This method reloads a single zone's signed content -- re-signing it locally
    ///     (Primary/CloudPrimary) or re-reading its already-transferred content back out of
    ///     storage (a secondary-flavor zone, right after
    ///     <see cref="SignedZoneChangedEventSubscriber" /> reacts to the very
    ///     <c>DnsZoneChangedEvent</c> a completed AXFR/IXFR pull just published) -- removing it
    ///     from this cache entirely if it no longer qualifies for either (deleted, disabled,
    ///     changed to an ineligible type, or no longer marked signed).
    /// </summary>
    /// <param name="zoneId">The identifier of the zone to reload.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    public async Task ReloadZoneAsync(Guid zoneId, CancellationToken stoppingToken = default)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IZoneRepository zoneRepository =
            scope.ServiceProvider.GetRequiredService<IZoneRepository>();

        ZoneEntity zone = await zoneRepository.GetByIdAsync(zoneId, stoppingToken);

        if (
            zone is
            {
                IsDisabled: false,
                Type: ZoneTypeEnum.Primary or ZoneTypeEnum.CloudPrimary,
                IsDnssecSigned: true,
            }
        )
        {
            // A zone cannot be both locally-signed and secondary-flavor at once -- clear the
            // other dictionary's entry too, in case this zone's own type just changed away from
            // one and into the other since it was last cached.
            _transferredSignedZonesByZoneId.TryRemove(zoneId, out _);
            await TrySignAndStoreAsync(zoneId, zone.Name, stoppingToken);
        }
        else if (
            zone is not null
            && !zone.IsDisabled
            && IsTransferredSignedZoneType(zone.Type)
            && zone.IsDnssecSigned
        )
        {
            _signedZonesByZoneId.TryRemove(zoneId, out _);
            await TryLoadTransferredSignedDataAsync(zoneId, zone.Name, stoppingToken);
        }
        else
        {
            _signedZonesByZoneId.TryRemove(zoneId, out _);
            _transferredSignedZonesByZoneId.TryRemove(zoneId, out _);
        }
    }

    /// <summary>
    ///     This method performs a schedule-driven, proactive re-sign for a zone whose signatures
    ///     are approaching expiry -- called only by
    ///     <see cref="DnssecResigningBackgroundService" />, never in reaction to an ordinary
    ///     record change (<see cref="SignedZoneChangedEventSubscriber" />'s own
    ///     <see cref="ReloadZoneAsync" /> call already covers that case, and must never also bump
    ///     the serial itself, since the record change that triggered it already did). Real-world
    ///     DNS convention treats a re-sign as a genuine content change -- a secondary's stale
    ///     RRSIGs are exactly as wrong as a stale record -- so this bumps the zone's own SOA
    ///     serial through <see cref="IZoneUpdateService.BumpSerialForDnssecResignAsync" /> before
    ///     re-signing, matching this project's own confirmed decision that a resign is a real
    ///     change a secondary must notice.
    /// </summary>
    /// <param name="zoneId">The identifier of the zone to re-sign.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    public async Task ResignForScheduledExpiryAsync(
        Guid zoneId,
        CancellationToken stoppingToken = default
    )
    {
        await using (AsyncServiceScope scope = scopeFactory.CreateAsyncScope())
        {
            IZoneUpdateService zoneUpdateService =
                scope.ServiceProvider.GetRequiredService<IZoneUpdateService>();

            try
            {
                await zoneUpdateService.BumpSerialForDnssecResignAsync(zoneId, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // The zone may have been deleted, disabled, or unmarked-signed since it was last
                // cached -- ReloadZoneAsync below still runs regardless, to clear this cache
                // entry the same way it already would for any other now-ineligible zone; only
                // the serial bump itself is skipped.
                logger.LogWarning(
                    exception,
                    "Could not bump the SOA serial while proactively re-signing zone '{ZoneId}'; re-checking its eligibility instead.",
                    zoneId
                );
            }
        }

        // Re-sign explicitly, right away, rather than relying solely on the DnsZoneChangedEvent
        // the bump above just published -- this project's events are delivered asynchronously off
        // a background channel (see DnsServerEventPublisher), so waiting on that alone would leave
        // this cache serving stale signatures for an indeterminate interval after the serial has
        // already moved. SignedZoneChangedEventSubscriber's own reaction to that same event
        // re-signs this zone all over again shortly afterward regardless -- a harmless, idempotent
        // duplicate of the explicit reload this method already performs, cheap purely local CPU
        // work, the same tradeoff this class's own constructor doc comment already accepts for
        // every other resign path.
        await ReloadZoneAsync(zoneId, stoppingToken);
    }

    /// <summary>
    ///     This method signs a single zone and stores the result, replacing whatever this cache
    ///     currently holds for it. A failure (most commonly a signed zone with no active key yet)
    ///     is logged rather than thrown, so it cannot take down the loop signing every other zone
    ///     -- the previously cached signed data, if any, is left untouched rather than cleared.
    /// </summary>
    private async Task TrySignAndStoreAsync(
        Guid zoneId,
        string zoneName,
        CancellationToken stoppingToken
    )
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IZoneSigningService signingService =
                scope.ServiceProvider.GetRequiredService<IZoneSigningService>();

            SignedZoneData signedData = await signingService.SignZoneAsync(zoneId, stoppingToken);

            _signedZonesByZoneId[zoneId] = signedData;

            // Publish only after a genuinely successful sign -- never from the catch block below,
            // and never for a zone this check skipped entirely (see ReloadZoneAsync's own
            // else-branch removal path).
            eventPublisher?.Publish(
                new DnssecZoneResigningCompletedEvent(
                    DateTimeOffset.UtcNow,
                    zoneId,
                    zoneName,
                    signedData.SignatureExpiresAtUtc
                )
            );
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Failed to sign zone '{ZoneId}'.", zoneId);
        }
    }

    /// <summary>
    ///     This method loads a single secondary-flavor zone's own already-transferred DNSSEC
    ///     content straight out of storage and caches it, replacing whatever this cache currently
    ///     holds for it -- the secondary-side counterpart to <see cref="TrySignAndStoreAsync" />.
    ///     Never signs anything, and never touches a <see cref="DnssecKeyEntity" /> or any private
    ///     key material at all; see <see cref="TransferredSignedZoneDataBuilder" /> for what
    ///     "load" actually means here. A failure is logged rather than thrown, exactly like
    ///     <see cref="TrySignAndStoreAsync" />'s own failure handling, so one zone's own storage or
    ///     decode problem cannot take down every other zone this loop is loading. Deliberately
    ///     never publishes <see cref="DnssecZoneResigningCompletedEvent" /> the way
    ///     <see cref="TrySignAndStoreAsync" /> does -- that event's own name and meaning ("this
    ///     instance just resigned this zone") would be actively wrong for a zone this instance
    ///     never signs at all.
    /// </summary>
    private async Task TryLoadTransferredSignedDataAsync(
        Guid zoneId,
        string zoneName,
        CancellationToken stoppingToken
    )
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IRecordRepository recordRepository =
                scope.ServiceProvider.GetRequiredService<IRecordRepository>();

            IReadOnlyList<RecordEntity> storedRecords = await recordRepository.ListByZoneIdAsync(
                zoneId,
                stoppingToken
            );

            SignedZoneData signedData = TransferredSignedZoneDataBuilder.Build(
                DnsName.Parse(zoneName),
                storedRecords
            );

            // No DNSSEC content has actually arrived for this zone yet (a signed secondary,
            // freshly configured, before its very first transfer completes) -- leave whatever
            // this cache already held for it untouched, the same "log/skip, don't clear" shape
            // TrySignAndStoreAsync's own failure handling above already follows; ReloadZoneAsync's
            // own else-branch is what actually clears a genuinely ineligible zone's entry.
            if (signedData is not null)
                _transferredSignedZonesByZoneId[zoneId] = signedData;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Failed to load transferred DNSSEC content for zone '{ZoneId}'.",
                zoneId
            );
        }
    }

    /// <summary>
    ///     This method reports whether a zone's own type is one whose signed DNSSEC content this
    ///     cache loads by reading it back from already-transferred <see cref="RecordEntity" />
    ///     rows, rather than by computing it locally -- every answerable secondary-flavor
    ///     <see cref="ZoneTypeEnum" /> value <see cref="Cache.AuthoritativeZoneCache" /> itself
    ///     also answers from (see that class's own <c>IsAnswerableZoneType</c>), minus the two
    ///     locally-signed, primary-flavor ones <see cref="TrySignAndStoreAsync" /> already covers.
    ///     <see cref="ZoneTypeEnum.CloudSecondary" /> is included here for the same
    ///     future-proofing reason it is included in <c>IsAnswerableZoneType</c>, even though this
    ///     project's cloud connectors do not currently transfer any DNSSEC-specific record type at
    ///     all (see <c>CloudDnsRecordMapper</c>'s own 8-type list) -- a zone of this type simply
    ///     never has any DNSKEY rows for <see cref="TransferredSignedZoneDataBuilder.Build" /> to
    ///     find, so loading it is a safe no-op today.
    /// </summary>
    private static bool IsTransferredSignedZoneType(ZoneTypeEnum zoneType) =>
        zoneType
            is ZoneTypeEnum.Secondary
                or ZoneTypeEnum.SecondaryCatalog
                or ZoneTypeEnum.CloudSecondary;
}
