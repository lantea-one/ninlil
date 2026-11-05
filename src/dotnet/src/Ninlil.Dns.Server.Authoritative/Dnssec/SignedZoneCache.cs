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
namespace Ninlil.Dns.Server.Authoritative.Dnssec;

/// <summary>
///     This class holds every signed zone's <see cref="SignedZoneData" /> in memory, next to
///     <see cref="Cache.AuthoritativeZoneCache" />'s record data. Primary and CloudPrimary zones
///     are signed here with <see cref="IZoneSigningService" />; Secondary, SecondaryCatalog and
///     CloudSecondary zones have their transferred DNSSEC records read back from storage (see
///     <see cref="TransferredSignedZoneDataBuilder" />). The resolver and zone transfer read it
///     through <see cref="GetSignedData" />. It loads every zone at startup, and reloads a zone on
///     <see cref="DnsZoneChangedEvent" /> (<see cref="SignedZoneChangedEventSubscriber" />), on
///     the re-signing schedule, on key rollover steps and when
///     <see cref="Cache.ZoneCacheBackstopService" /> finds that another instance changed it.
///     <para>
///         A reload that changes which keys of a zone get a DS record (<see cref="DelegationDsKeys" />)
///         also re-signs the closest parent hosted here (<see cref="LocalParentLocator" />), because
///         the parent generates that DS when it is signed. This covers every key change an
///         operator makes, whatever path made it, as long as the zone is reloaded afterwards.
///     </para>
/// </summary>
/// <param name="scopeFactory">
///     The factory used to reach the repositories and signing service, one scope per load so no
///     database context is held longer than one zone.
/// </param>
/// <param name="logger">The logger this cache records a single zone's signing failure to.</param>
/// <param name="eventPublisher">
///     The publisher for <see cref="DnssecZoneResigningCompletedEvent" />, raised after each
///     successful local signing. With <see langword="null" />, nothing is published.
/// </param>
/// <param name="zoneCache">
///     The cache of the zones hosted here, read to find a zone's local parent. With
///     <see langword="null" />, no parent is re-signed.
/// </param>
public sealed class SignedZoneCache(
    IServiceScopeFactory scopeFactory,
    ILogger<SignedZoneCache> logger,
    IDnsServerEventPublisher eventPublisher = null,
    AuthoritativeZoneCache zoneCache = null
) : IHostedService
{
    /// <summary>
    ///     This field contains the zones signed here (Primary and CloudPrimary), keyed by ID. Only
    ///     these appear in <see cref="CachedZoneIds" />, which the re-signing and key rollover
    ///     services walk.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, SignedZoneData> _signedZonesByZoneId = new();

    /// <summary>
    ///     This field contains the IDs of zones that are marked signed and signed here, but have
    ///     no key that can sign, including a zone whose keys are all Removed or that has none (<see cref="IZoneSigningService.SignZoneAsync" /> returned
    ///     <see langword="null" />). They have no entry in <see cref="_signedZonesByZoneId" />, so
    ///     they are served unsigned, yet <see cref="CachedZoneIds" /> still lists them: the
    ///     rollover and re-signing services keep visiting them, so adding or rolling to a key with
    ///     a permitted algorithm makes the zone signed again.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, byte> _unsignableZoneIds = new();

    /// <summary>
    ///     This field contains secondary zones whose DNSSEC records arrived signed by a zone
    ///     transfer (Secondary, SecondaryCatalog, CloudSecondary), rebuilt by
    ///     <see cref="TransferredSignedZoneDataBuilder" />. They are kept apart so
    ///     <see cref="CachedZoneIds" /> never lists them: this server has no keys to re-sign them
    ///     and must not bump the serial of a zone it does not own. <see cref="GetSignedData" />
    ///     reads both dictionaries.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, SignedZoneData> _transferredSignedZonesByZoneId =
        new();

    /// <summary>
    ///     This field contains, for each zone signed here, its name and the
    ///     <see cref="DelegationDsKeys.Fingerprint" /> of its keys at the last reload. A reload
    ///     that finds a different fingerprint re-signs the local parent. The name is kept because
    ///     a deleted zone can no longer be read when its parent must be found.
    /// </summary>
    private readonly ConcurrentDictionary<
        Guid,
        (DnsName Name, string Fingerprint)
    > _delegationDsStateByZoneId = new();

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // A plain scope reads the primary. This cache is loaded at startup, after a zone or key
        // change and when ZoneCacheBackstopService finds a zone another instance changed. Each
        // of those follows a write that a replica may not have yet. Do not use
        // CreateReplicaReadScope here.
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IZoneRepository zoneRepository =
            scope.ServiceProvider.GetRequiredService<IZoneRepository>();

        // Sign zones this server is the source for: Primary, and CloudPrimary (pushed to the
        // cloud provider from here). Secondary zones arrive already signed by their primary and
        // are loaded by the second loop below.
        IReadOnlyList<ZoneEntity> signedZones = await zoneRepository.ListAsync(
            zone =>
                !zone.IsDisabled
                && (zone.Type == ZoneTypeEnum.Primary || zone.Type == ZoneTypeEnum.CloudPrimary)
                && zone.IsDnssecSigned,
            cancellationToken
        );

        // TrySignAndStoreAsync logs failures, so one bad zone (a corrupt key, for example) does
        // not stop the others.
        foreach (ZoneEntity zone in signedZones)
        {
            await TrySignAndStoreAsync(zone.Id, zone.Name, cancellationToken);

            // Record the starting point only. Each parent signs from the database, which already
            // holds every child's keys.
            await UpdateDelegationDsStateAsync(zone.Id, zone.Name, true, cancellationToken);
        }

        // Load signed secondary zones already in storage, or they would serve no RRSIGs until
        // their next transfer.
        //
        // The type check is spelled out instead of calling IsTransferredSignedZoneType because
        // EF Core cannot translate a C# method call to SQL and throws at query time.
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
    ///     This method returns a zone's signed content from either dictionary, so
    ///     <see cref="Resolution.AuthoritativeQueryResolver" /> and
    ///     <see cref="Transfer.ZoneTransferService" /> do not need to know which kind of zone it
    ///     is.
    /// </summary>
    /// <param name="zoneId">The identifier of the zone to look up.</param>
    /// <returns>
    ///     The zone's signed content, or <see langword="null" /> if the zone is not signed or has
    ///     not been signed (or, for a secondary, transferred) yet.
    /// </returns>
    public SignedZoneData GetSignedData(Guid zoneId) =>
        _signedZonesByZoneId.TryGetValue(zoneId, out SignedZoneData signedData)
            ? signedData
            : _transferredSignedZonesByZoneId.GetValueOrDefault(zoneId);

    /// <summary>
    ///     This property contains the IDs of the zones signed here (not the transferred ones),
    ///     including those marked signed that have no key able to sign and are served unsigned.
    ///     <see cref="DnssecResigningBackgroundService" /> and
    ///     <see cref="DnssecKeyRolloverBackgroundService" /> walk it.
    /// </summary>
    public IReadOnlyCollection<Guid> CachedZoneIds =>
        [.. _signedZonesByZoneId.Keys.Union(_unsignableZoneIds.Keys)];

    /// <summary>
    ///     This method reloads one zone: it re-signs a Primary or CloudPrimary zone, or re-reads a
    ///     secondary's transferred DNSSEC records (as after a completed AXFR/IXFR). A zone that no
    ///     longer qualifies (deleted, disabled, another type, or not signed) is removed.
    /// </summary>
    /// <param name="zoneId">The identifier of the zone to reload.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    public async Task ReloadZoneAsync(Guid zoneId, CancellationToken stoppingToken = default)
    {
        Guid? parentZoneId = await ReloadSingleZoneAsync(zoneId, stoppingToken);

        // One level is enough: re-signing the parent does not change the parent's own keys.
        if (parentZoneId.HasValue)
            await ReloadSingleZoneAsync(parentZoneId.Value, stoppingToken);
    }

    /// <summary>
    ///     This method reloads one zone without touching any other, as
    ///     <see cref="ReloadZoneAsync" /> describes.
    /// </summary>
    /// <returns>
    ///     The identifier of the local parent to re-sign because the zone's DS-relevant keys
    ///     changed, or <see langword="null" /> when they did not or no parent is hosted here.
    /// </returns>
    private async Task<Guid?> ReloadSingleZoneAsync(Guid zoneId, CancellationToken stoppingToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IZoneRepository zoneRepository =
            scope.ServiceProvider.GetRequiredService<IZoneRepository>();

        ZoneEntity zone = await zoneRepository.GetByIdAsync(zoneId, stoppingToken);
        DnsName changedName;

        if (
            zone is
            {
                IsDisabled: false,
                Type: ZoneTypeEnum.Primary or ZoneTypeEnum.CloudPrimary,
                IsDnssecSigned: true,
            }
        )
        {
            // The zone's type may have changed, so drop any entry in the other dictionary.
            _transferredSignedZonesByZoneId.TryRemove(zoneId, out _);
            await TrySignAndStoreAsync(zoneId, zone.Name, stoppingToken);
            changedName = await UpdateDelegationDsStateAsync(
                zoneId,
                zone.Name,
                true,
                stoppingToken
            );
        }
        else if (
            zone is not null
            && !zone.IsDisabled
            && IsTransferredSignedZoneType(zone.Type)
            && zone.IsDnssecSigned
        )
        {
            _signedZonesByZoneId.TryRemove(zoneId, out _);
            _unsignableZoneIds.TryRemove(zoneId, out _);
            await TryLoadTransferredSignedDataAsync(zoneId, zone.Name, stoppingToken);
            changedName = await UpdateDelegationDsStateAsync(
                zoneId,
                zone.Name,
                false,
                stoppingToken
            );
        }
        else
        {
            _signedZonesByZoneId.TryRemove(zoneId, out _);
            _unsignableZoneIds.TryRemove(zoneId, out _);
            _transferredSignedZonesByZoneId.TryRemove(zoneId, out _);
            changedName = await UpdateDelegationDsStateAsync(
                zoneId,
                zone?.Name,
                false,
                stoppingToken
            );
        }

        return changedName is null || zoneCache is null
            ? null
            : LocalParentLocator.Find(zoneCache, changedName, zoneId).ParentZoneId;
    }

    /// <summary>
    ///     This method re-signs a zone whose signatures are close to expiring.
    ///     <see cref="DnssecResigningBackgroundService" /> calls it. New RRSIGs are a content change
    ///     secondaries must pick up, so it first bumps the SOA serial through
    ///     <see cref="IZoneUpdateService.BumpSerialForDnssecResignAsync" />. Re-signing after a
    ///     record change goes through <see cref="ReloadZoneAsync" /> instead, which does not bump
    ///     the serial because the change already did.
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
                // The zone may have been deleted, disabled or unsigned since it was cached.
                // ReloadZoneAsync below still runs and removes it if so.
                logger.LogWarning(
                    exception,
                    "Could not bump the SOA serial while proactively re-signing zone '{ZoneId}'; re-checking its eligibility instead.",
                    zoneId
                );
            }
        }

        // Re-sign now instead of waiting for the DnsZoneChangedEvent the bump published, which
        // is delivered later in the background; until then the new serial would be served with
        // old signatures. The event still triggers a second, harmless re-sign.
        await ReloadZoneAsync(zoneId, stoppingToken);
    }

    /// <summary>
    ///     This method signs one zone, stores the result and publishes
    ///     <see cref="DnssecZoneResigningCompletedEvent" />. A failure (a storage error, for
    ///     example) is logged, not thrown, and the previous signed data is kept. When the signing
    ///     service returns <see langword="null" /> (no key can sign, or no key is left), the zone's signed data is
    ///     removed, the zone ID is kept in <see cref="_unsignableZoneIds" />, one Warning is
    ///     logged and no event is published.
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

            if (signedData is null)
            {
                // Serve the zone unsigned: a DNSKEY RRset with no signatures would be bogus. The
                // ID stays listed so a later key change signs the zone again. The remedy for an
                // external parent is in the message because its DS cannot match any served key.
                _signedZonesByZoneId.TryRemove(zoneId, out _);
                _unsignableZoneIds[zoneId] = 0;
                logger.LogWarning(
                    "Zone '{ZoneName}' ({ZoneId}) is marked signed but has no key that can sign; it is served unsigned until a key with a permitted algorithm is added. If a parent zone published a DS record for it, replace or remove that DS.",
                    zoneName,
                    zoneId
                );
                return;
            }

            _signedZonesByZoneId[zoneId] = signedData;
            _unsignableZoneIds.TryRemove(zoneId, out _);

            // Published only after a successful sign.
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
    ///     This method stores the zone's current <see cref="DelegationDsKeys.Fingerprint" /> and
    ///     reports whether it differs from the stored one, which means the DS RRset its parent
    ///     publishes is stale. A zone that is not signed here (unsigned, disabled, deleted, a
    ///     secondary) has an empty fingerprint, so it loses its DS in the parent. It reads the
    ///     keys from storage, which is one query per reload of a zone signed here. A storage
    ///     failure is logged and counts as no change.
    /// </summary>
    /// <param name="zoneId">The identifier of the zone.</param>
    /// <param name="zoneName">The zone's name, or <see langword="null" /> when it no longer exists.</param>
    /// <param name="signedHere">Whether this server signs the zone (Primary or CloudPrimary).</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The zone's name when its fingerprint changed, otherwise <see langword="null" />.</returns>
    private async Task<DnsName> UpdateDelegationDsStateAsync(
        Guid zoneId,
        string zoneName,
        bool signedHere,
        CancellationToken stoppingToken
    )
    {
        try
        {
            string fingerprint = string.Empty;

            if (signedHere)
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                fingerprint = DelegationDsKeys.Fingerprint(
                    await scope
                        .ServiceProvider.GetRequiredService<IDnssecKeyRepository>()
                        .ListByZoneIdAsync(zoneId, stoppingToken)
                );
            }

            _delegationDsStateByZoneId.TryGetValue(
                zoneId,
                out (DnsName Name, string Fingerprint) previous
            );
            string previousFingerprint = previous.Fingerprint ?? string.Empty;

            // Only a zone signed here keeps an entry; the name of a zone that was removed comes
            // from the entry it leaves behind.
            DnsName name = zoneName is null ? previous.Name : DnsName.Parse(zoneName);

            if (signedHere)
                _delegationDsStateByZoneId[zoneId] = (name, fingerprint);
            else
                _delegationDsStateByZoneId.TryRemove(zoneId, out _);

            return name is not null && fingerprint != previousFingerprint ? name : null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Failed to compare the DS-relevant keys of zone '{ZoneId}'; its parent is not re-signed.",
                zoneId
            );

            return null;
        }
    }

    /// <summary>
    ///     This method reads a secondary zone's transferred DNSSEC records from storage, rebuilds
    ///     them with <see cref="TransferredSignedZoneDataBuilder" /> and caches the result: the
    ///     secondary counterpart of <see cref="TrySignAndStoreAsync" />. It never signs or uses a
    ///     key. Failures are logged, not thrown. It publishes no
    ///     <see cref="DnssecZoneResigningCompletedEvent" />, since this server did not sign the
    ///     zone.
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

            // Null means no DNSSEC records have arrived yet (before the first transfer), so keep
            // what is cached. ReloadZoneAsync removes zones that no longer qualify.
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
    ///     This method reports whether a zone type's DNSSEC content is read from transferred
    ///     <see cref="RecordEntity" /> rows rather than signed here: the secondary types
    ///     <see cref="Cache.AuthoritativeZoneCache" /> answers from. CloudSecondary is included
    ///     although the cloud connectors import no DNSSEC record types (see
    ///     <c>CloudDnsRecordMapper</c>), so for it loading finds no DNSKEY and does nothing.
    /// </summary>
    private static bool IsTransferredSignedZoneType(ZoneTypeEnum zoneType) =>
        zoneType
            is ZoneTypeEnum.Secondary
                or ZoneTypeEnum.SecondaryCatalog
                or ZoneTypeEnum.CloudSecondary;
}
