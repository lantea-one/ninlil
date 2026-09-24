using System.Collections.Concurrent;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Cache;

/// <summary>
///     This class holds one zone's records in memory, indexed for the lookups
///     <see cref="Resolution.AuthoritativeQueryResolver" /> needs on every query: an exact name
///     match, a wildcard match, and a delegation (zone-cut) check. It is rebuilt in full whenever
///     the zone changes, rather than updated incrementally -- a zone rarely has more than a few
///     thousand records, so rebuilding is cheap, and it keeps this class simple.
/// </summary>
public sealed class CachedZone
{
    /// <summary>
    ///     This property contains this zone's own database record.
    /// </summary>
    public ZoneEntity Zone { get; }

    /// <summary>
    ///     This property contains this zone's name, parsed once rather than on every lookup.
    /// </summary>
    public DnsName ApexName { get; }

    /// <summary>
    ///     This property indexes every enabled record by its exact owner name.
    /// </summary>
    private readonly Dictionary<DnsName, List<RecordEntity>> _recordsByName;

    /// <summary>
    ///     This property tracks, per (name, type) pair with more than one record, how many times
    ///     that record set has been served, so <see cref="GetRecords" /> can rotate which record
    ///     comes first each time -- the round-robin load-balancing convention most authoritative
    ///     servers apply by default.
    /// </summary>
    private readonly ConcurrentDictionary<
        (DnsName Name, DnsResourceRecordTypeEnum Type),
        int
    > _roundRobinCounters = new();

    /// <summary>
    ///     This method builds the cache for a single zone from its full, current set of records.
    /// </summary>
    /// <param name="zone">The zone being cached.</param>
    /// <param name="records">Every record stored in the zone, enabled or not.</param>
    public CachedZone(ZoneEntity zone, IReadOnlyList<RecordEntity> records)
    {
        Zone = zone;
        ApexName = DnsName.Parse(zone.Name);

        _recordsByName = records
            .Where(record => !record.IsDisabled)
            .GroupBy(record => DnsName.Parse(record.Name))
            .ToDictionary(group => group.Key, group => group.ToList());
    }

    /// <summary>
    ///     This method looks up whether any ancestor of <paramref name="name" />, up to but not
    ///     including this zone's own apex, delegates a subzone elsewhere by way of NS records --
    ///     the RFC 1035 §4.3.2 "zone cut" a query below a delegation must be referred across,
    ///     rather than answered directly.
    /// </summary>
    /// <param name="name">The name to check for an enclosing delegation.</param>
    /// <returns>
    ///     The delegation point's own name and its NS records, or <see langword="null" /> if
    ///     <paramref name="name" /> is not delegated away from this zone.
    /// </returns>
    public (
        DnsName DelegationPoint,
        IReadOnlyList<RecordEntity> NameServers
    )? FindEnclosingDelegation(DnsName name)
    {
        // Walk upward starting from name itself -- the name being queried for can be exactly the
        // delegation point -- up to, but excluding, this zone's own apex. The apex is excluded
        // deliberately: its own NS records simply list who is authoritative for *this* zone, not
        // a delegation of it to somewhere else, and every zone has them.
        for (int start = 0; start < name.Labels.Count - ApexName.Labels.Count; start++)
        {
            DnsName ancestor = new([.. name.Labels.Skip(start)]);

            if (
                _recordsByName.TryGetValue(ancestor, out List<RecordEntity> recordsAtAncestor)
                && recordsAtAncestor
                    .Where(record => record.Type == DnsResourceRecordTypeEnum.NS)
                    .ToList()
                    is { Count: > 0 } nameServers
            )
                return (ancestor, nameServers);
        }

        return null;
    }

    /// <summary>
    ///     This method looks up every enabled record at an exact owner name, optionally narrowed
    ///     to one type, rotating which record comes first when more than one exists.
    /// </summary>
    /// <param name="name">The exact owner name to look up.</param>
    /// <param name="type">
    ///     An optional record type filter; when <see langword="null" />, every type at that name
    ///     is returned.
    /// </param>
    /// <returns>The matching records, in the order they should be answered in.</returns>
    public IReadOnlyList<RecordEntity> GetRecords(
        DnsName name,
        DnsResourceRecordTypeEnum? type = null
    )
    {
        if (!_recordsByName.TryGetValue(name, out List<RecordEntity> recordsAtName))
            return [];

        List<RecordEntity> matching = type is { } requestedType
            ? [.. recordsAtName.Where(record => record.Type == requestedType)]
            : recordsAtName;

        return matching.Count <= 1 ? matching : Rotate(name, matching[0].Type, matching);
    }

    /// <summary>
    ///     This method lists every enabled record in the zone, at every name, with no rotation
    ///     and no type filtering -- used for a full zone transfer (AXFR), which must serve a
    ///     complete, exact, deterministic snapshot rather than the load-balanced view
    ///     <see cref="GetRecords" /> gives an ordinary query.
    /// </summary>
    /// <returns>Every enabled record stored in the zone.</returns>
    public IEnumerable<RecordEntity> GetAllRecords() => _recordsByName.Values.SelectMany(x => x);

    /// <summary>
    ///     This method checks whether any enabled record at all exists at an exact owner name,
    ///     regardless of type -- used to distinguish an empty non-terminal (RFC 1035/RFC 2308: a
    ///     name that exists, just not with any records of its own) from a name that does not
    ///     exist.
    /// </summary>
    /// <param name="name">The exact owner name to check.</param>
    /// <returns>A <see langword="bool" /> indicating whether the name exists in this zone.</returns>
    public bool NameExists(DnsName name) => _recordsByName.ContainsKey(name);

    /// <summary>
    ///     This method checks whether <paramref name="name" /> is an "empty non-terminal" per RFC
    ///     1035 §3.1 / RFC 2308 §5: a name with no records of its own, but that still exists in
    ///     the zone's namespace because some other stored name is a descendant of it (for
    ///     example, <c>b.example.com</c> exists this way if only <c>a.b.example.com</c> has
    ///     records). Such a name must answer NODATA, not NXDOMAIN, and must not have a wildcard
    ///     synthesized for it -- a wildcard only ever covers a name that does not exist at all.
    /// </summary>
    /// <param name="name">The exact owner name to check.</param>
    /// <returns>
    ///     A <see langword="bool" /> indicating whether any stored name in this zone is a proper
    ///     descendant of <paramref name="name" />.
    /// </returns>
    public bool HasDescendantRecords(DnsName name) =>
        _recordsByName
            .Keys.Where(candidate => candidate.Labels.Count > name.Labels.Count)
            .Any(candidate =>
                new DnsName([.. candidate.Labels.Skip(candidate.Labels.Count - name.Labels.Count)])
                == name
            );

    /// <summary>
    ///     This method looks up a wildcard record set that would answer for
    ///     <paramref name="name" />, per RFC 1035 §4.3.3: the closest wildcard at or above
    ///     <paramref name="name" />'s immediate parent, at or below this zone's apex.
    /// </summary>
    /// <param name="name">The name being queried for, which itself has no exact match.</param>
    /// <param name="type">
    ///     An optional record type filter; when <see langword="null" />, every type at the
    ///     matching wildcard is returned.
    /// </param>
    /// <returns>The matching wildcard's records, or an empty list if no wildcard applies.</returns>
    public IReadOnlyList<RecordEntity> GetWildcardRecords(
        DnsName name,
        DnsResourceRecordTypeEnum? type = null
    )
    {
        for (int start = 1; start <= name.Labels.Count - ApexName.Labels.Count; start++)
        {
            DnsName wildcardName = new([
                [(byte)'*'],
                .. name.Labels.Skip(start),
            ]);
            IReadOnlyList<RecordEntity> records = GetRecords(wildcardName, type);

            if (records.Count > 0)
                return records;
        }

        return [];
    }

    /// <summary>
    ///     This method rotates a multi-record answer, advancing a per-(name, type) counter each
    ///     time it is called.
    /// </summary>
    private IReadOnlyList<RecordEntity> Rotate(
        DnsName name,
        DnsResourceRecordTypeEnum type,
        IReadOnlyList<RecordEntity> records
    )
    {
        int servedCount = _roundRobinCounters.AddOrUpdate(
            (name, type),
            1,
            (_, previousCount) => previousCount + 1
        );

        int startIndex = (servedCount - 1) % records.Count;

        return [.. records.Skip(startIndex), .. records.Take(startIndex)];
    }
}
