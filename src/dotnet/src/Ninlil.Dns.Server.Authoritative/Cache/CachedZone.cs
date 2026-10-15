using System.Collections.Concurrent;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Cache;

/// <summary>
///     This class holds one zone's enabled records in memory, indexed for the lookups
///     <see cref="Resolution.AuthoritativeQueryResolver" /> makes on every query: exact name,
///     wildcard (the source of synthesis at the closest encloser) and delegation (zone cut). <see cref="AuthoritativeZoneCache" /> rebuilds it in
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
    ///     This field holds every owner name with an enabled record plus each of its ancestors
    ///     down to the apex (the empty non-terminals), for <see cref="NameExistsOrIsEmptyNonTerminal" />.
    /// </summary>
    private readonly HashSet<DnsName> _existingNames;

    /// <summary>
    ///     This field maps each topmost zone cut (a non-apex owner of NS records with no
    ///     NS-owning ancestor below the apex) to its NS records, for
    ///     <see cref="FindEnclosingDelegation" />.
    /// </summary>
    private readonly Dictionary<DnsName, List<RecordEntity>> _delegationPoints;

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

        _existingNames = [];
        foreach (DnsName owner in _recordsByName.Keys)
        {
            // Add the owner and every ancestor down to the apex. Stop at the first name already
            // present: its ancestors were added with it.
            for (int depth = owner.Labels.Count; depth >= ApexName.Labels.Count; depth--)
                if (
                    !_existingNames.Add(
                        new DnsName([.. owner.Labels.Skip(owner.Labels.Count - depth)])
                    )
                )
                    break;
        }

        // Collect every non-apex NS owner, then drop the ones below another NS owner: only the
        // topmost cuts stay, because data below a cut is the child's (RFC 1034 §4.2.1).
        Dictionary<DnsName, List<RecordEntity>> nsOwners = _recordsByName
            .Where(entry => entry.Key != ApexName)
            .Select(entry =>
                (
                    Name: entry.Key,
                    NameServers: entry
                        .Value.Where(record => record.Type == DnsResourceRecordTypeEnum.NS)
                        .ToList()
                )
            )
            .Where(entry => entry.NameServers.Count > 0)
            .ToDictionary(entry => entry.Name, entry => entry.NameServers);

        _delegationPoints = nsOwners
            .Where(entry =>
                !Enumerable
                    .Range(
                        ApexName.Labels.Count + 1,
                        entry.Key.Labels.Count - ApexName.Labels.Count - 1
                    )
                    .Any(depth =>
                        nsOwners.ContainsKey(
                            new DnsName([.. entry.Key.Labels.Skip(entry.Key.Labels.Count - depth)])
                        )
                    )
            )
            .ToDictionary(entry => entry.Key, entry => entry.Value);
    }

    /// <summary>
    ///     This method finds the topmost zone cut at or above <paramref name="name" />: the
    ///     shallowest name below the apex that owns NS records (RFC 1034 §4.2.1, §4.3.2 step 3b).
    ///     Everything at or below a cut belongs to the child, so a cut nested inside another
    ///     (<c>a.b.example</c> under <c>b.example</c>) is occluded and is never returned.
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
        // Walk from the label just below the apex down to name itself, which may be the cut.
        // The apex NS records name this zone's servers, not a delegation, so the walk starts
        // below it. The first hit is the topmost cut.
        for (int depth = ApexName.Labels.Count + 1; depth <= name.Labels.Count; depth++)
        {
            DnsName ancestor = new([.. name.Labels.Skip(name.Labels.Count - depth)]);

            if (_delegationPoints.TryGetValue(ancestor, out List<RecordEntity> nameServers))
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
    ///     This method checks whether a name exists in this zone: it owns an enabled record, or it
    ///     is an empty non-terminal because a stored name lies below it (for example
    ///     <c>b.example.com</c> when only <c>a.b.example.com</c> has records, RFC 4592 §2.2.2).
    ///     Such a name answers NODATA, not NXDOMAIN, and no wildcard is synthesized for it. The
    ///     answer comes from an index built when the zone was cached, so the lookup is O(1).
    /// </summary>
    /// <param name="name">The exact name to check.</param>
    /// <returns>
    ///     A <see langword="bool" /> indicating whether the name owns records or is an empty
    ///     non-terminal.
    /// </returns>
    public bool NameExistsOrIsEmptyNonTerminal(DnsName name) => _existingNames.Contains(name);

    /// <summary>
    ///     This method finds the closest encloser of a name that does not exist (RFC 4592 §3.3.1):
    ///     the longest ancestor that exists in this zone, meaning it owns records or is an empty
    ///     non-terminal. The apex always exists, so the walk ends there at the latest. The caller
    ///     passes only a name that does not exist and is not at or below a zone cut.
    /// </summary>
    /// <param name="name">The name being queried for.</param>
    /// <returns>The closest encloser, or the apex if no closer ancestor exists.</returns>
    public DnsName FindClosestEncloser(DnsName name)
    {
        for (int depth = name.Labels.Count - 1; depth > ApexName.Labels.Count; depth--)
        {
            DnsName ancestor = new([.. name.Labels.Skip(name.Labels.Count - depth)]);

            if (_existingNames.Contains(ancestor))
                return ancestor;
        }

        return ApexName;
    }

    /// <summary>
    ///     This method looks up the records at the source of synthesis for a name, the wildcard
    ///     <c>*.closestEncloser</c> (RFC 4592 §3.3.1). Only this one wildcard can answer for the
    ///     name: a wildcard further up never applies, because the closest encloser stops the
    ///     search (RFC 4592 §2.1.3).
    /// </summary>
    /// <param name="closestEncloser">The closest encloser from <see cref="FindClosestEncloser" />.</param>
    /// <param name="type">
    ///     An optional record type filter; when <see langword="null" />, every type at the
    ///     wildcard is returned.
    /// </param>
    /// <returns>The wildcard's records, or an empty list if the wildcard does not exist.</returns>
    public IReadOnlyList<RecordEntity> GetSourceOfSynthesisRecords(
        DnsName closestEncloser,
        DnsResourceRecordTypeEnum? type = null
    ) =>
        GetRecords(
            new DnsName([
                [(byte)'*'],
                .. closestEncloser.Labels,
            ]),
            type
        );

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
