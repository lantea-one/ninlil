using System.Collections.Concurrent;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Cache;

/// <summary>
///     This class holds one zone's enabled records in memory, indexed for the lookups
///     <see cref="Resolution.AuthoritativeQueryResolver" /> makes on every query: exact name,
///     wildcard and delegation (zone cut). <see cref="AuthoritativeZoneCache" /> rebuilds it in
///     full whenever the zone changes; zones are small enough that a rebuild is cheap, and it
///     keeps this class simple.
/// </summary>
public sealed class CachedZone
{
    /// <summary>
    ///     This property contains the zone's database record.
    /// </summary>
    public ZoneEntity Zone { get; }

    /// <summary>
    ///     This property contains this zone's name, parsed once rather than on every lookup.
    /// </summary>
    public DnsName ApexName { get; }

    /// <summary>
    ///     This field indexes every enabled record by its owner name.
    /// </summary>
    private readonly Dictionary<DnsName, List<RecordEntity>> _recordsByName;

    /// <summary>
    ///     This field counts, per (name, type) with more than one record, how often the set was
    ///     served, so <see cref="GetRecords" /> can rotate which record comes first (round-robin,
    ///     as most authoritative servers do).
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
    ///     This method finds a delegation (NS records at <paramref name="name" /> or an ancestor,
    ///     below the apex) that the query must be referred across rather than answered (the
    ///     RFC 1035 §4.3.2 zone cut). It checks from <paramref name="name" /> upward and returns
    ///     the first NS set found, which is the deepest one if several are nested.
    /// </summary>
    /// <param name="name">The name to check for an enclosing delegation.</param>
    /// <returns>
    ///     The delegation point's name and its NS records, or <see langword="null" /> if
    ///     <paramref name="name" /> is not delegated away from this zone.
    /// </returns>
    public (
        DnsName DelegationPoint,
        IReadOnlyList<RecordEntity> NameServers
    )? FindEnclosingDelegation(DnsName name)
    {
        // Start at name itself, which may be the delegation point, and stop below the apex: the
        // apex NS records name this zone's servers, not a delegation.
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
    ///     This method lists every enabled record in the zone without rotation or filtering, for
    ///     a full zone transfer (AXFR), which needs the complete set rather than the rotated view
    ///     of <see cref="GetRecords" />.
    /// </summary>
    /// <returns>Every enabled record stored in the zone.</returns>
    public IEnumerable<RecordEntity> GetAllRecords() => _recordsByName.Values.SelectMany(x => x);

    /// <summary>
    ///     This method checks whether any enabled record exists at an owner name, of any type.
    /// </summary>
    /// <param name="name">The exact owner name to check.</param>
    /// <returns>A <see langword="bool" /> indicating whether the name exists in this zone.</returns>
    public bool NameExists(DnsName name) => _recordsByName.ContainsKey(name);

    /// <summary>
    ///     This method checks whether any stored name is below <paramref name="name" />, which
    ///     makes a name with no records of its an empty non-terminal (for example
    ///     <c>b.example.com</c> when only <c>a.b.example.com</c> has records). Such a name answers
    ///     NODATA, not NXDOMAIN, and no wildcard is synthesized for it. It scans every name in the
    ///     zone.
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
    ///     This method looks for wildcard records that could answer for <paramref name="name" />
    ///     (RFC 1035 §4.3.3): it tries <c>*.parent</c> for each ancestor from the immediate parent
    ///     up to the apex and returns the first match. With <paramref name="type" /> set, it
    ///     returns the first wildcard that has that type. It does not stop at the closest
    ///     existing ancestor (the RFC 4592 closest encloser).
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
