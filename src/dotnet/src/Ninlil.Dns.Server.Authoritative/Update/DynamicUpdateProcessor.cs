using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Server.Authoritative.Cache;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Update;

/// <summary>
///     This class holds the logic of RFC 2136 dynamic update (prerequisite checks, update
///     prescan and computing the record diff) apart from <see cref="DynamicUpdateQueryResolver" />,
///     so it can be tested with plain data. RFC 2136's Prerequisite section is
///     <see cref="DnsMessage.Answers" /> and its Update section is
///     <see cref="DnsMessage.AuthorityRecords" />. The prerequisite check and the prescan reject
///     owner names outside the zone with NOTZONE; the diff assumes the prescan passed.
/// </summary>
public static class DynamicUpdateProcessor
{
    /// <summary>
    ///     This method checks every prerequisite in an update request against the zone's current
    ///     records, per RFC 2136 §3.2.
    /// </summary>
    /// <param name="prerequisites">The prerequisite section (the request's Answer section).</param>
    /// <param name="currentRecords">The zone's current records, before any update is applied.</param>
    /// <param name="zoneApexName">The zone's apex name; every prerequisite must lie at or below it.</param>
    /// <returns>
    ///     <see cref="DnsResponseCodeEnum.NoError" /> if every prerequisite holds, otherwise the
    ///     RFC 2136 response code for the first one that fails: FORMERR for a prerequisite with a
    ///     non-zero TTL, an unknown class or RDATA on a CLASS ANY or NONE record, NOTZONE for an
    ///     owner outside the zone, then NXDOMAIN, YXDOMAIN, NXRRSET or YXRRSET.
    /// </returns>
    public static DnsResponseCodeEnum CheckPrerequisites(
        IReadOnlyList<DnsResourceRecord> prerequisites,
        IReadOnlyList<RecordEntity> currentRecords,
        DnsName zoneApexName
    )
    {
        // RFC 2136 §3.2.1 to §3.2.5: the form of every prerequisite is checked before any value
        // is compared with the zone, so a malformed section never depends on zone content.
        foreach (DnsResourceRecord prerequisite in prerequisites)
        {
            if (prerequisite.TimeToLive != 0)
                return DnsResponseCodeEnum.FormatError;

            if (!IsAtOrBelow(prerequisite.Name, zoneApexName))
                return DnsResponseCodeEnum.NotZone;

            if (
                prerequisite.Class is DnsClassEnum.Any or DnsClassEnum.None
                    ? prerequisite.Data.Length != 0
                    : prerequisite.Class != DnsClassEnum.Internet
            )
                return DnsResponseCodeEnum.FormatError;
        }

        List<DnsResourceRecord> currentWire = ToWireRecords(currentRecords);

        // Value-dependent prerequisites are grouped by name and type: RFC 2136 §2.4.2 requires
        // the zone's RRset to equal the listed records, not just contain them.
        Dictionary<
            (DnsName Name, DnsResourceRecordTypeEnum Type),
            List<DnsResourceRecord>
        > valueDependentGroups = [];

        foreach (DnsResourceRecord prerequisite in prerequisites)
        {
            if (
                prerequisite.Type == DnsResourceRecordTypeEnum.Any
                && prerequisite.Class == DnsClassEnum.Any
            )
            {
                // Name is in use.
                if (!currentWire.Any(record => record.Name == prerequisite.Name))
                    return DnsResponseCodeEnum.NxDomain;
            }
            else if (
                prerequisite.Type == DnsResourceRecordTypeEnum.Any
                && prerequisite.Class == DnsClassEnum.None
            )
            {
                // Name is not in use.
                if (currentWire.Any(record => record.Name == prerequisite.Name))
                    return DnsResponseCodeEnum.YxDomain;
            }
            else if (prerequisite.Class == DnsClassEnum.Any)
            {
                // RRset exists (value-independent).
                if (
                    !currentWire.Any(record =>
                        record.Name == prerequisite.Name && record.Type == prerequisite.Type
                    )
                )
                    return DnsResponseCodeEnum.NxRrSet;
            }
            else if (prerequisite.Class == DnsClassEnum.None)
            {
                // RRset does not exist.
                if (
                    currentWire.Any(record =>
                        record.Name == prerequisite.Name && record.Type == prerequisite.Type
                    )
                )
                    return DnsResponseCodeEnum.YxRrSet;
            }
            else
            {
                (DnsName, DnsResourceRecordTypeEnum) key = (prerequisite.Name, prerequisite.Type);

                if (!valueDependentGroups.TryGetValue(key, out List<DnsResourceRecord> group))
                    valueDependentGroups[key] = group = [];

                group.Add(prerequisite);
            }
        }

        foreach (
            (
                (DnsName name, DnsResourceRecordTypeEnum type),
                List<DnsResourceRecord> required
            ) in valueDependentGroups
        )
        {
            List<DnsResourceRecord> actual =
            [
                .. currentWire.Where(record => record.Name == name && record.Type == type),
            ];

            if (
                actual.Count != required.Count
                || required.Any(requiredRecord =>
                    !actual.Any(actualRecord => RecordContentEquals(actualRecord, requiredRecord))
                )
            )
                return DnsResponseCodeEnum.NxRrSet;
        }

        return DnsResponseCodeEnum.NoError;
    }

    /// <summary>
    ///     This method runs the RFC 2136 §3.4.1 prescan: each update RR must lie inside the zone
    ///     and have the shape of one of the four §2.5 operations.
    /// </summary>
    /// <param name="updates">The update section (the request's Authority section).</param>
    /// <param name="zoneClass">The zone's class, in practice <see cref="DnsClassEnum.Internet" />.</param>
    /// <param name="zoneApexName">The zone's apex name.</param>
    /// <returns>
    ///     <see cref="DnsResponseCodeEnum.NoError" /> if every RR passes, NOTZONE for the first
    ///     owner outside the zone, FORMERR for the first malformed RR.
    /// </returns>
    public static DnsResponseCodeEnum PrescanUpdateSection(
        IReadOnlyList<DnsResourceRecord> updates,
        DnsClassEnum zoneClass,
        DnsName zoneApexName
    )
    {
        foreach (DnsResourceRecord update in updates)
        {
            if (!IsAtOrBelow(update.Name, zoneApexName))
                return DnsResponseCodeEnum.NotZone;

            if (update.Class == zoneClass)
            {
                // Add to an RRset: a concrete type only.
                if (IsMetaType(update.Type) || update.Type == DnsResourceRecordTypeEnum.Any)
                    return DnsResponseCodeEnum.FormatError;
            }
            else if (update.Class == DnsClassEnum.Any)
            {
                // Delete an RRset, or every RRset at a name (type ANY): no RDATA and TTL 0.
                if (update.TimeToLive != 0 || update.Data.Length != 0 || IsMetaType(update.Type))
                    return DnsResponseCodeEnum.FormatError;
            }
            else if (update.Class == DnsClassEnum.None)
            {
                // Delete one RR: TTL 0, with the RDATA to match, and a concrete type.
                if (
                    update.TimeToLive != 0
                    || IsMetaType(update.Type)
                    || update.Type == DnsResourceRecordTypeEnum.Any
                )
                    return DnsResponseCodeEnum.FormatError;
            }
            else
            {
                return DnsResponseCodeEnum.FormatError;
            }
        }

        return DnsResponseCodeEnum.NoError;
    }

    /// <summary>
    ///     This method applies the update section in order to a working copy of the zone's records
    ///     and returns the net difference, which the caller passes to
    ///     <see cref="Journal.IZoneUpdateService.ApplyChangesAsync" />.
    /// </summary>
    /// <param name="updates">The update section (the request's Authority section).</param>
    /// <param name="currentRecords">The zone's current records, before any update is applied.</param>
    /// <param name="zoneId">The identifier of the zone being updated.</param>
    /// <param name="zoneApexName">The zone's apex name.</param>
    /// <returns>
    ///     The records to add and remove to realize the update section's net effect.
    /// </returns>
    public static (List<RecordEntity> ToAdd, List<RecordEntity> ToRemove) ComputeDiff(
        IReadOnlyList<DnsResourceRecord> updates,
        IReadOnlyList<RecordEntity> currentRecords,
        Guid zoneId,
        DnsName zoneApexName
    )
    {
        List<(RecordEntity Entity, DnsResourceRecord Wire)> working =
        [
            .. currentRecords
                .Select(record =>
                    (record, RecordEntityCodec.ToWireRecord(record, DnsName.Parse(record.Name)))
                )
                .Where(pair => pair.Item2 is not null),
        ];

        foreach (DnsResourceRecord update in updates)
        {
            // SOA updates are ignored. IZoneUpdateService advances the SOA serial itself, and
            // adding or removing SOA rows here could leave zero or two SOA records.
            if (update.Type == DnsResourceRecordTypeEnum.Soa)
                continue;

            bool isApex = update.Name == zoneApexName;

            if (update.Type == DnsResourceRecordTypeEnum.Any && update.Class == DnsClassEnum.Any)
            {
                // Delete every RRset at this name, except SOA and NS at the apex (RFC 2136
                // §3.4.2.3).
                working.RemoveAll(pair =>
                    pair.Wire.Name == update.Name
                    && !(
                        isApex
                        && (
                            pair.Wire.Type == DnsResourceRecordTypeEnum.Soa
                            || pair.Wire.Type == DnsResourceRecordTypeEnum.NS
                        )
                    )
                );
            }
            else if (update.Class == DnsClassEnum.Any)
            {
                // Delete the whole RRset of this type. The apex NS RRset is never deleted
                // (RFC 2136 §3.4.2.3); the SOA is skipped above.
                if (isApex && update.Type == DnsResourceRecordTypeEnum.NS)
                    continue;

                working.RemoveAll(pair =>
                    pair.Wire.Name == update.Name && pair.Wire.Type == update.Type
                );
            }
            else if (update.Class == DnsClassEnum.None)
            {
                // Delete one specific RR. The last NS RR at the apex stays (RFC 2136 §3.4.2.4).
                if (
                    isApex
                    && update.Type == DnsResourceRecordTypeEnum.NS
                    && working.Count(pair =>
                        pair.Wire.Name == update.Name
                        && pair.Wire.Type == DnsResourceRecordTypeEnum.NS
                    ) <= 1
                )
                    continue;

                working.RemoveAll(pair => RecordContentEquals(pair.Wire, update));
            }
            else
            {
                // Add to an RRset (RFC 2136 §3.4.2.2). The CNAME rules come first: a CNAME is
                // ignored where other data exists and other data is ignored where a CNAME
                // exists. RRSIG and NSEC may sit beside a CNAME, because DNSSEC requires it.
                bool isCname = update.Type == DnsResourceRecordTypeEnum.Cname;
                bool coexistsWithCname =
                    update.Type
                    is DnsResourceRecordTypeEnum.RrSig
                        or DnsResourceRecordTypeEnum.Nsec;
                IEnumerable<DnsResourceRecord> atName = working
                    .Select(pair => pair.Wire)
                    .Where(wire => wire.Name == update.Name);

                if (
                    isCname
                    && atName.Any(wire =>
                        wire.Type
                            is not (
                                DnsResourceRecordTypeEnum.Cname
                                or DnsResourceRecordTypeEnum.RrSig
                                or DnsResourceRecordTypeEnum.Nsec
                            )
                    )
                )
                    continue;

                if (
                    !isCname
                    && !coexistsWithCname
                    && atName.Any(wire => wire.Type == DnsResourceRecordTypeEnum.Cname)
                )
                    continue;

                // A CNAME replaces the one that exists; an RR equal in owner, type and RDATA is
                // replaced so the update's TTL wins. FromWireRecord always assigns a new Id, so
                // a replace is a remove of the old row and an add of the new one. An identical
                // TTL changes nothing and is skipped to avoid a journal entry.
                (RecordEntity Entity, DnsResourceRecord Wire) existing = working.FirstOrDefault(
                    pair =>
                        isCname
                            ? pair.Wire.Name == update.Name
                                && pair.Wire.Type == DnsResourceRecordTypeEnum.Cname
                            : RecordContentEquals(pair.Wire, update)
                );

                if (existing.Entity is not null)
                {
                    if (
                        existing.Wire.TimeToLive == update.TimeToLive
                        && existing.Wire.Data.AsSpan().SequenceEqual(update.Data)
                    )
                        continue;

                    working.Remove(existing);
                }

                RecordEntity added = RecordEntityCodec.FromWireRecord(update, zoneId);

                if (added is not null)
                    working.Add((added, update));
            }
        }

        HashSet<Guid> originalIds = [.. currentRecords.Select(record => record.Id)];
        HashSet<Guid> finalIds = [.. working.Select(pair => pair.Entity.Id)];

        List<RecordEntity> toAdd =
        [
            .. working
                .Where(pair => !originalIds.Contains(pair.Entity.Id))
                .Select(pair => pair.Entity),
        ];
        List<RecordEntity> toRemove =
        [
            .. currentRecords.Where(record => !finalIds.Contains(record.Id)),
        ];

        return (toAdd, toRemove);
    }

    /// <summary>
    ///     This method reports whether <paramref name="name" /> is the zone apex or lies below it,
    ///     the RFC 2136 test for "within the zone". The zone's own cut-outs are not considered.
    /// </summary>
    private static bool IsAtOrBelow(DnsName name, DnsName zoneApexName)
    {
        int skip = name.Labels.Count - zoneApexName.Labels.Count;

        return skip >= 0 && new DnsName([.. name.Labels.Skip(skip)]) == zoneApexName;
    }

    /// <summary>
    ///     This method reports whether a type is a query-only meta type that RFC 2136 §3.4.1
    ///     forbids in an update: IXFR, AXFR, MAILB (253) or MAILA (254). ANY is handled by the
    ///     caller, because CLASS ANY allows it.
    /// </summary>
    private static bool IsMetaType(DnsResourceRecordTypeEnum type) =>
        type is DnsResourceRecordTypeEnum.Ixfr or DnsResourceRecordTypeEnum.Axfr
        || (ushort)type is 253 or 254;

    /// <summary>
    ///     This method compares two records as RFC 2136 value matching does: by owner name, type
    ///     and RDATA, ignoring TTL and CLASS. A delete RR has TTL 0, and RFC 2136 uses CLASS as an
    ///     operation marker (<see cref="DnsClassEnum.Any" />, <see cref="DnsClassEnum.None" />),
    ///     while stored records are always <see cref="DnsClassEnum.Internet" />; comparing CLASS
    ///     would stop a delete by value (CLASS NONE) from matching anything.
    /// </summary>
    private static bool RecordContentEquals(DnsResourceRecord a, DnsResourceRecord b) =>
        a.Name == b.Name && a.Type == b.Type && a.Data.AsSpan().SequenceEqual(b.Data);

    /// <summary>
    ///     This method converts stored records to wire form for comparison.
    /// </summary>
    private static List<DnsResourceRecord> ToWireRecords(IReadOnlyList<RecordEntity> records) =>
        [
            .. records
                .Select(record =>
                    RecordEntityCodec.ToWireRecord(record, DnsName.Parse(record.Name))
                )
                .Where(record => record is not null),
        ];
}
