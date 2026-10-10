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
///     This class holds every signed zone's <see cref="SignedZoneData" /> in memory, next to
///     <see cref="Cache.AuthoritativeZoneCache" />'s record data. Primary and CloudPrimary zones
///     are signed here with <see cref="IZoneSigningService" />; Secondary, SecondaryCatalog and
///     CloudSecondary zones have their transferred DNSSEC records read back from storage (see
///     <see cref="TransferredSignedZoneDataBuilder" />). The resolver and zone transfer read it
///     through <see cref="GetSignedData" />. It loads every zone at startup, and reloads a zone on
///     <see cref="DnsZoneChangedEvent" /> (<see cref="SignedZoneChangedEventSubscriber" />), on
///     the re-signing schedule and on key rollover steps.
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
public sealed class SignedZoneCache(
    IServiceScopeFactory scopeFactory,
    ILogger<SignedZoneCache> logger,
    IDnsServerEventPublisher eventPublisher = null
) : IHostedService
{
    /// <summary>
    ///     This field contains the zones signed here (Primary and CloudPrimary), keyed by ID. Only
    ///     these appear in <see cref="CachedZoneIds" />, which the re-signing and key rollover
    ///     services walk.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, SignedZoneData> _signedZonesByZoneId = new();

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

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // A plain scope reads the primary. This cache is loaded at startup and after a zone or
        // key change, never on a timer, so a replica may not have the data yet. Do not use
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

        // TrySignAndStoreAsync logs failures, so one bad zone (signed but with no key, for
        // example) does not stop the others.
        foreach (ZoneEntity zone in signedZones)
            await TrySignAndStoreAsync(zone.Id, zone.Name, cancellationToken);

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
    ///     This property contains the IDs of the zones signed here (not the transferred ones).
    ///     <see cref="DnssecResigningBackgroundService" /> and
    ///     <see cref="DnssecKeyRolloverBackgroundService" /> walk it.
    /// </summary>
    public IReadOnlyCollection<Guid> CachedZoneIds => [.. _signedZonesByZoneId.Keys];

    /// <summary>
    ///     This method reloads one zone: it re-signs a Primary or CloudPrimary zone, or re-reads a
    ///     secondary's transferred DNSSEC records (as after a completed AXFR/IXFR). A zone that no
    ///     longer qualifies (deleted, disabled, another type, or not signed) is removed.
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
            // The zone's type may have changed, so drop any entry in the other dictionary.
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
    ///     <see cref="DnssecZoneResigningCompletedEvent" />. A failure (usually a signed zone with
    ///     no key) is logged, not thrown, and the previous signed data is kept.
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
