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
///     This class holds every zone this server hosts, in memory, as a set of <see cref="CachedZone" />
///     instances keyed by zone name -- the one thing standing between
///     <see cref="Resolution.AuthoritativeQueryResolver" /> and a database round trip on every
///     single query it answers. The cache is loaded in full when the application starts, and a
///     single zone is reloaded on demand whenever <c>Ninlil.Dns.Server.Core.Events.DnsZoneChangedEvent</c>
///     reports that it changed.
/// </summary>
/// <param name="scopeFactory">
///     The factory used to reach the repositories this cache loads from, in a scope of its own so
///     it never holds a database context open longer than a single load.
/// </param>
public sealed class AuthoritativeZoneCache(IServiceScopeFactory scopeFactory) : IHostedService
{
    /// <summary>
    ///     This property contains every currently-cached zone, keyed by its name.
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

        // Primary, secondary, cloud-connector-synced, and catalog zones are all served directly
        // out of this cache -- a secondary (classic RFC 5936/1996, or REST-connector-backed
        // CloudSecondary) answers from its own, kept-in-sync copy of the data exactly the way a
        // primary (classic, or CloudPrimary) answers from its own locally-edited copy, and RFC
        // 1035 draws no distinction between any of these from a querying client's point of
        // view. A Catalog/SecondaryCatalog zone is, per RFC 9432, "a regular DNS zone" too -- its
        // own version/member records must be genuinely answerable and transferable exactly like
        // any other zone's content (ZoneTransferService's own AXFR/IXFR responder looks a zone up
        // through this same cache), even though an operator is expected to restrict who can query
        // it. Stub/forwarder zone types remain out of scope (delegation-only data and conditional
        // forwarding, neither of which is ordinary answerable zone content). This filter is the
        // one place in the whole engine most likely to silently fall behind a newly-answerable
        // ZoneTypeEnum value: a type missing here is never loaded, with no error at all.
        //
        // IsAnswerableZoneType itself is applied client-side, after the (translatable) IsDisabled
        // filter narrows what the database sends back -- a real, one-time discovery from writing
        // this fix: EF Core's LINQ provider cannot translate an arbitrary static C# method call
        // (even one this simple) into SQL, so passing it directly inside the query predicate
        // throws InvalidOperationException at query time, not at compile time. In-memory
        // filtering afterward is the same "fetch, then filter" trade-off this project's own
        // repositories already make for pagination.
        IReadOnlyList<ZoneEntity> everyEnabledZone = await zoneRepository.ListAsync(
            zone => !zone.IsDisabled,
            cancellationToken
        );

        foreach (ZoneEntity zone in everyEnabledZone.Where(zone => IsAnswerableZoneType(zone.Type)))
            await LoadZoneAsync(zone, recordRepository, cancellationToken);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    ///     This method finds the closest zone this server hosts that is authoritative for a
    ///     name -- the zone whose own name is the longest suffix of <paramref name="name" /> that
    ///     this cache actually has, per RFC 1035 §4.3.2.
    ///     <para>
    ///         An NS or DS query is a deliberate exception to "longest suffix wins": per RFC 4035
    ///         §2.7/RFC 3658 (DS) and ordinary delegation semantics (NS), both belong to whichever
    ///         zone actually *delegates* the name away, even when this same server also happens to
    ///         separately host a zone under that exact name for the delegated-to data itself. Left
    ///         unhandled, a same-server parent+child setup would see every DS/NS query at the
    ///         delegation point swallowed by the child zone's own (DS/delegation-less) data,
    ///         instead of ever reaching the parent's real answer -- a real gap this method closes
    ///         by continuing the walk past an *exact* match to look for an enclosing zone that
    ///         genuinely delegates <paramref name="name" /> via its own NS records, and preferring
    ///         that zone for these two types specifically.
    ///     </para>
    /// </summary>
    /// <param name="name">The name to find the enclosing zone for.</param>
    /// <param name="queryType">
    ///     The query's own type, or <see langword="null" /> when the caller has none to offer (a
    ///     zone transfer's own apex-name match, for example, which needs no such exception). Only
    ///     <see cref="DnsResourceRecordTypeEnum.NS" /> and <see cref="DnsResourceRecordTypeEnum.DS" />
    ///     trigger the exception above; every other type resolves exactly as before.
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

            // An exact match (start == 0): normally the answer, but NS/DS defer to a genuinely
            // delegating ancestor first, if one exists -- checked below rather than returned
            // immediately.
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
    ///     This method reloads a single zone from the database, replacing whatever this cache
    ///     currently holds for it -- or removing it from the cache entirely, if it has been
    ///     deleted or disabled since it was last loaded.
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
            // The replacement is loaded *before* anything about the existing entry is touched,
            // so FindZone never sees a moment where this zone is entirely absent from the cache
            // -- a real gap the naive "remove, then reload" order left open: between the removal
            // and the still-pending database read the replacement needs, any query concurrently
            // asking about this zone (an ordinary lookup, an inbound NOTIFY, a dynamic update)
            // would have found nothing hosting it at all.
            await LoadZoneAsync(zone, recordRepository, stoppingToken);

            // Only now clean up a *different*, stale entry left behind by a rename.
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
            // The zone was deleted, disabled, or changed to a type this cache does not serve;
            // there is no replacement to load first, so simply remove whatever is cached for it.
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
    ///     This method determines whether a zone's own <see cref="ZoneEntity.Type" /> is one this
    ///     cache ever loads or reloads -- the single source of truth <see cref="StartAsync" /> and
    ///     <see cref="ReloadZoneAsync" /> both defer to, deliberately, so a future answerable
    ///     <see cref="ZoneTypeEnum" /> value only ever needs to be added in this one place:
    ///     before this method existed, the two call sites each hardcoded
    ///     their own copy of this list, and it was easy to update one without the other.
    ///     <para>
    ///         <see cref="ZoneTypeEnum.Forwarder" />/<see cref="ZoneTypeEnum.SecondaryForwarder" />
    ///         are included here too, but "answerable" for them means only their own synthetic
    ///         SOA/TXT apex content (the wire-transferable encoding of
    ///         <see cref="ZoneEntity.ForwarderAddresses" /> -- see
    ///         <c>Forwarding.ForwarderZoneContentSyncService</c>) is cache-resident, so
    ///         <see cref="Transfer.ZoneTransferService" />'s own AXFR/IXFR path (and, for a
    ///         <see cref="ZoneTypeEnum.SecondaryForwarder" /> zone,
    ///         <c>Refresh.SecondaryZoneRefreshService</c>'s own pull) can find it.
    ///         <see cref="Resolution.AuthoritativeQueryResolver" /> deliberately never answers an
    ///         ordinary (non-AXFR/IXFR) query against either of these two zone types from this
    ///         cache at all -- see its own doc comment -- so a name under a Forwarder zone is
    ///         still always forwarded, exactly as before this apex content existed.
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
