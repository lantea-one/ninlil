using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Server.Authoritative.Cache;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Update;

/// <summary>
///     This class implements the pure logic behind RFC 2136 dynamic update -- prerequisite
///     checking, update-section validation, and diff computation -- kept separate from
///     <see cref="DynamicUpdateQueryResolver" /> so it needs nothing beyond plain data to test.
///     RFC 2136 reuses the ordinary message format under different names for its middle two
///     sections: what it calls the Prerequisite section is <see cref="DnsMessage.Answers" />, and
///     what it calls the Update section is <see cref="DnsMessage.AuthorityRecords" />.
/// </summary>
public static class DynamicUpdateProcessor
{
    /// <summary>
    ///     This method checks every prerequisite in an update request against the zone's current
    ///     records, per RFC 2136 §3.2.
    /// </summary>
    /// <param name="prerequisites">The prerequisite section (the request's Answer section).</param>
    /// <param name="currentRecords">The zone's current records, before any update is applied.</param>
    /// <returns>
    ///     <see cref="DnsResponseCodeEnum.NoError" /> if every prerequisite is satisfied, or the
    ///     specific RFC 2136 response code identifying which kind of prerequisite failed first.
    /// </returns>
    public static DnsResponseCodeEnum CheckPrerequisites(
        IReadOnlyList<DnsResourceRecord> prerequisites,
        IReadOnlyList<RecordEntity> currentRecords
    )
    {
        List<DnsResourceRecord> currentWire = ToWireRecords(currentRecords);

        // Value-dependent prerequisites sharing the same owner name and type are grouped: per
        // RFC 2136 §2.4.2, the zone's RRset for that name and type must contain *exactly* the
        // union of the listed records, not merely include them among others.
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
    ///     This method checks that every update RR is shaped like one of RFC 2136 §2.5's four
    ///     recognized operations, per the prescan §3.3 describes.
    /// </summary>
    /// <param name="updates">The update section (the request's Authority section).</param>
    /// <param name="zoneClass">The zone's own class -- in practice, always <see cref="DnsClassEnum.Internet" />.</param>
    public static bool IsUpdateSectionValid(
        IReadOnlyList<DnsResourceRecord> updates,
        DnsClassEnum zoneClass
    )
    {
        foreach (DnsResourceRecord update in updates)
        {
            if (update.Class == zoneClass)
                continue; // Add to an RRset.

            if (update.Class == DnsClassEnum.Any)
            {
                // Delete an RRset, or every RRset at a name -- both carry no RDATA and no TTL.
                if (update.TimeToLive != 0 || update.Data.Length != 0)
                    return false;

                continue;
            }

            if (update.Class == DnsClassEnum.None)
            {
                // Delete one specific RR -- no TTL, but it does carry the RDATA to match.
                if (update.TimeToLive != 0)
                    return false;

                continue;
            }

            return false;
        }

        return true;
    }

    /// <summary>
    ///     This method applies an update section, in order, against a working copy of the zone's
    ///     current records, then reports the net difference against the original -- the actual
    ///     write <see cref="Journal.IZoneUpdateService.ApplyChangesAsync" /> should perform.
    /// </summary>
    /// <param name="updates">The update section (the request's Authority section).</param>
    /// <param name="currentRecords">The zone's current records, before any update is applied.</param>
    /// <param name="zoneId">The identifier of the zone being updated.</param>
    /// <param name="zoneApexName">The zone's own apex name.</param>
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
            // SOA is managed exclusively by IZoneUpdateService itself, which advances the
            // existing SOA row's own serial as part of applying whatever this diff computes;
            // letting an update add or remove SOA rows directly here would conflict with that
            // and could leave the zone with zero or two SOA records. RFC 2136 clients essentially
            // never try to touch the SOA directly in practice; this is a deliberate no-op for it.
            if (update.Type == DnsResourceRecordTypeEnum.Soa)
                continue;

            if (update.Type == DnsResourceRecordTypeEnum.Any && update.Class == DnsClassEnum.Any)
            {
                // Delete every RRset at this name -- except SOA/NS at the zone's own apex, per
                // RFC 2136 §2.5.3.
                bool isApex = update.Name == zoneApexName;

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
                // Delete the whole RRset of this type.
                working.RemoveAll(pair =>
                    pair.Wire.Name == update.Name && pair.Wire.Type == update.Type
                );
            }
            else if (update.Class == DnsClassEnum.None)
            {
                // Delete one specific RR.
                working.RemoveAll(pair => RecordContentEquals(pair.Wire, update));
            }
            else
            {
                // Add to an RRset. A CNAME is exclusive -- RFC 2136 §3.4.2.2 -- so adding a
                // second one at a name that already has one is a no-op, the same as adding an
                // exact duplicate of an already-present record of any other type.
                bool cnameAlreadyExists =
                    update.Type == DnsResourceRecordTypeEnum.Cname
                    && working.Any(pair =>
                        pair.Wire.Name == update.Name
                        && pair.Wire.Type == DnsResourceRecordTypeEnum.Cname
                    );

                if (
                    cnameAlreadyExists
                    || working.Any(pair => RecordContentEquals(pair.Wire, update))
                )
                    continue;

                working.Add((RecordEntityCodec.FromWireRecord(update, zoneId), update));
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
    ///     This method compares two records the same way RFC 2136's specific-value matching
    ///     does: by owner name, type, and RDATA -- deliberately ignoring both TTL and CLASS.
    ///     TTL is ignored because a delete RR is always sent with TTL 0 regardless of the stored
    ///     record's real TTL. CLASS is ignored for the same reason: RFC 2136 repurposes it as an
    ///     operation marker (<see cref="DnsClassEnum.Any" />/<see cref="DnsClassEnum.None" />) on
    ///     a delete or value-independent prerequisite RR, not as a description of the actual
    ///     record's class (which, for every record this project stores, is always
    ///     <see cref="DnsClassEnum.Internet" /> regardless). Comparing CLASS directly would mean
    ///     a delete-by-value RR (CLASS NONE) could never match anything at all.
    /// </summary>
    private static bool RecordContentEquals(DnsResourceRecord a, DnsResourceRecord b) =>
        a.Name == b.Name && a.Type == b.Type && a.Data.AsSpan().SequenceEqual(b.Data);

    /// <summary>
    ///     This method converts a set of stored records into wire form for comparison purposes.
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
