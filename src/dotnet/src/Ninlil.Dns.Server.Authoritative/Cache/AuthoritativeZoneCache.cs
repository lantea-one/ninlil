using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Cache;

/// <summary>
///     This class holds every hosted zone in memory as a <see cref="CachedZone" />, keyed by zone
///     name, so <see cref="Resolution.AuthoritativeQueryResolver" /> and
///     <see cref="Transfer.ZoneTransferService" /> never query the database per request. It loads
///     every answerable zone at startup, and <see cref="ZoneChangedEventSubscriber" /> reloads one
///     zone on each <c>DnsZoneChangedEvent</c>.
/// </summary>
/// <param name="scopeFactory">
///     The factory used to reach the repositories, one scope per load so no database context is
///     held longer than needed.
/// </param>
public sealed class AuthoritativeZoneCache(IServiceScopeFactory scopeFactory) : IHostedService
{
    /// <summary>
    ///     This field contains every cached zone, keyed by name.
    /// </summary>
    private readonly ConcurrentDictionary<DnsName, CachedZone> _zonesByName = new();

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IZoneRepository zoneRepository =
            scope.ServiceProvider.GetRequiredService<IZoneRepository>();
        IRecordRepository recordRepository =
            scope.ServiceProvider.GetRequiredService<IRecordRepository>();

        // See IsAnswerableZoneType for which types load. It is applied in memory after the
        // IsDisabled filter because EF Core cannot translate a C# method call to SQL and would
        // throw at query time.
        IReadOnlyList<ZoneEntity> everyEnabledZone = await zoneRepository.ListAsync(
            zone => !zone.IsDisabled,
            cancellationToken
        );

        List<ZoneEntity> answerableZones =
        [
            .. everyEnabledZone.Where(zone => IsAnswerableZoneType(zone.Type)),
        ];

        // One query for the records of every zone, not one per zone, so the number of queries at
        // startup does not grow with the number of zones.
        IReadOnlyList<RecordEntity> everyRecord = await recordRepository.ListByZoneIdsAsync(
            [.. answerableZones.Select(zone => zone.Id)],
            cancellationToken
        );
        ILookup<Guid, RecordEntity> recordsByZoneId = everyRecord.ToLookup(record => record.ZoneId);

        foreach (ZoneEntity zone in answerableZones)
            _zonesByName[DnsName.Parse(zone.Name)] = new CachedZone(
                zone,
                [.. recordsByZoneId[zone.Id]]
            );
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    ///     This method finds the hosted zone for a name: the one whose name is the longest suffix
    ///     of <paramref name="name" /> (RFC 1035 §4.3.2).
    ///     <para>
    ///         NS and DS queries are the exception. When <paramref name="name" /> is itself a hosted
    ///         zone's apex and a hosted ancestor zone delegates it with NS records, the ancestor is
    ///         returned, because the DS belongs to the parent (RFC 4035 §2.7). Otherwise, with parent
    ///         and child on the same server, the child would answer DS queries it has no data for.
    ///     </para>
    /// </summary>
    /// <param name="name">The name to find the enclosing zone for.</param>
    /// <param name="queryType">
    ///     The query type, or <see langword="null" /> when there is none (a zone transfer looking up
    ///     its apex, for example). Only <see cref="DnsResourceRecordTypeEnum.NS" /> and
    ///     <see cref="DnsResourceRecordTypeEnum.DS" /> trigger the exception.
    /// </param>
    /// <returns>
    ///     The closest enclosing <see cref="CachedZone" />, or <see langword="null" /> if this
    ///     server hosts nothing that could answer for <paramref name="name" />.
    /// </returns>
    public CachedZone FindZone(DnsName name, DnsResourceRecordTypeEnum? queryType = null)
    {
        CachedZone exactMatch = null;

        for (int start = 0; start <= name.Labels.Count; start++)
        {
            DnsName candidate = new([.. name.Labels.Skip(start)]);

            if (!_zonesByName.TryGetValue(candidate, out CachedZone zone))
                continue;

            if (start > 0)
                return zone;

            // An exact match is normally the answer, but NS and DS check for a delegating
            // ancestor first (below).
            exactMatch = zone;
            break;
        }

        if (
            exactMatch is not null
            && queryType is DnsResourceRecordTypeEnum.NS or DnsResourceRecordTypeEnum.DS
        )
        {
            for (int start = 1; start <= name.Labels.Count; start++)
            {
                DnsName candidate = new([.. name.Labels.Skip(start)]);

                if (
                    _zonesByName.TryGetValue(candidate, out CachedZone ancestorZone)
                    && ancestorZone.GetRecords(name, DnsResourceRecordTypeEnum.NS).Count > 0
                )
                    return ancestorZone;
            }
        }

        return exactMatch;
    }

    /// <summary>
    ///     This method reloads one zone from the database, replacing the cached copy, or removes
    ///     it if it was deleted, disabled or changed to a type this cache does not hold.
    /// </summary>
    /// <param name="zoneId">The identifier of the zone to reload.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    public async Task ReloadZoneAsync(Guid zoneId, CancellationToken stoppingToken = default)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IZoneRepository zoneRepository =
            scope.ServiceProvider.GetRequiredService<IZoneRepository>();
        IRecordRepository recordRepository =
            scope.ServiceProvider.GetRequiredService<IRecordRepository>();

        ZoneEntity zone = await zoneRepository.GetByIdAsync(zoneId, stoppingToken);

        if (zone is not null && !zone.IsDisabled && IsAnswerableZoneType(zone.Type))
        {
            // Load the replacement before removing anything, so a concurrent lookup, NOTIFY or
            // dynamic update never finds the zone missing.
            await LoadZoneAsync(zone, recordRepository, stoppingToken);

            // Then remove an entry left under an old name by a rename.
            DnsName currentName = DnsName.Parse(zone.Name);

            foreach (
                DnsName existingName in _zonesByName
                    .Keys.Where(name => name != currentName && ZoneMatches(name, zoneId))
                    .ToList()
            )
                _zonesByName.TryRemove(existingName, out _);
        }
        else
        {
            // Deleted, disabled or no longer an answerable type: remove it.
            foreach (
                DnsName existingName in _zonesByName
                    .Keys.Where(name => ZoneMatches(name, zoneId))
                    .ToList()
            )
                _zonesByName.TryRemove(existingName, out _);
        }
    }

    /// <summary>
    ///     This method loads a single zone's records and adds it to the cache.
    /// </summary>
    private async Task LoadZoneAsync(
        ZoneEntity zone,
        IRecordRepository recordRepository,
        CancellationToken stoppingToken
    )
    {
        IReadOnlyList<RecordEntity> records = await recordRepository.ListByZoneIdAsync(
            zone.Id,
            stoppingToken
        );

        _zonesByName[DnsName.Parse(zone.Name)] = new CachedZone(zone, records);
    }

    /// <summary>
    ///     This method checks whether a cached zone name belongs to a specific zone identifier.
    /// </summary>
    private bool ZoneMatches(DnsName cachedName, Guid zoneId) =>
        _zonesByName.TryGetValue(cachedName, out CachedZone cachedZone)
        && cachedZone.Zone.Id == zoneId;

    /// <summary>
    ///     This method decides which zone types this cache holds; <see cref="StartAsync" /> and
    ///     <see cref="ReloadZoneAsync" /> both use it, so a new answerable
    ///     <see cref="ZoneTypeEnum" /> is added here once. A type missing here is never loaded,
    ///     with no error.
    ///     <para>
    ///         Primary, secondary and cloud zones answer from their data. Catalog and
    ///         SecondaryCatalog zones are ordinary zones under RFC 9432, so they are answerable and
    ///         transferable. Stub zones are not held. Forwarder and SecondaryForwarder zones are
    ///         held only for their synthetic apex SOA and TXT records (the encoded
    ///         <see cref="ZoneEntity.ForwarderAddresses" />, see
    ///         <c>Forwarding.ForwarderZoneContentSyncService</c>), which zone transfer and the
    ///         secondary refresh need; <see cref="Resolution.AuthoritativeQueryResolver" /> answers
    ///         only their apex SOA and forwards everything else.
    ///     </para>
    /// </summary>
    private static bool IsAnswerableZoneType(ZoneTypeEnum zoneType) =>
        zoneType
            is ZoneTypeEnum.Primary
                or ZoneTypeEnum.Secondary
                or ZoneTypeEnum.CloudPrimary
                or ZoneTypeEnum.CloudSecondary
                or ZoneTypeEnum.Catalog
                or ZoneTypeEnum.SecondaryCatalog
                or ZoneTypeEnum.Forwarder
                or ZoneTypeEnum.SecondaryForwarder;
}
