using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Dnssec;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Dnssec;

/// <summary>
///     This class builds a secondary-flavor zone's own <see cref="SignedZoneData" /> straight out
///     of whatever RRSIG/NSEC/NSEC3/NSEC3PARAM/DNSKEY/DS <see cref="RecordEntity" /> rows a real
///     zone transfer already wrote for it -- the "hold what arrived, don't sign it" counterpart to
///     <see cref="ZoneSigningService" />'s own from-scratch signing pass for a Primary/CloudPrimary
///     zone. This class never touches a <see cref="DnssecKeyEntity" /> or any private key material
///     at all, and never computes a single signature: every one of these records was already
///     computed and signed by whichever server actually owns this zone's private keys, and arrived
///     here exactly as transferred -- authenticated only by the TSIG-protected AXFR/IXFR channel
///     that carried it, never independently re-verified cryptographically, record by record, on
///     top of that.
///     <para>
///         This is purely a read-and-reshape step: every record it looks at was already validated
///         as *received correctly* by the TSIG check the transfer itself performed, and every
///         piece of ordering this class computes (the NSEC/NSEC3 chain's own canonical/hash order,
///         the RRSIG-to-covered-RRset grouping) is a deterministic function of that content, not a
///         new fact being invented -- a real zone transfer offers no guarantee that record order
///         on the wire (and, in turn, in storage) still matches the chain order a validator's own
///         "find the record covering this name" walk needs, so this class re-derives that order
///         itself exactly the way <see cref="ZoneSigningService" /> originally computed it, rather
///         than trusting whatever order storage happens to return records in.
///     </para>
/// </summary>
public static class TransferredSignedZoneDataBuilder
{
    /// <summary>
    ///     This method builds a zone's <see cref="SignedZoneData" /> from its currently stored
    ///     records, or returns <see langword="null" /> when nothing has actually arrived for it
    ///     yet -- a freshly marked-signed secondary zone, before its very first transfer has
    ///     completed, most commonly.
    /// </summary>
    /// <param name="apexName">The zone's own apex name.</param>
    /// <param name="storedRecords">Every record currently stored for the zone.</param>
    /// <returns>
    ///     The zone's transferred signed content, or <see langword="null" /> when no DNSKEY
    ///     record has arrived for it at all -- the same "nothing to serve yet" bar
    ///     <see cref="ZoneSigningService.SignZoneAsync" /> enforces for a Primary/CloudPrimary
    ///     zone with no active key, applied here to a secondary zone's own transferred content
    ///     instead.
    /// </returns>
    public static SignedZoneData Build(DnsName apexName, IReadOnlyList<RecordEntity> storedRecords)
    {
        // A disabled record was never actually served (nor, by the same logic the ordinary
        // record path already applies, ever meant to take part in what this zone is really
        // publishing) -- excluded here for the same reason ZoneSigningService excludes one from
        // its own signing pass.
        List<RecordEntity> activeRecords = [.. storedRecords.Where(record => !record.IsDisabled)];

        List<RecordEntity> dnsKeyEntities =
        [
            .. activeRecords.Where(record => record.Type == DnsResourceRecordTypeEnum.DnsKey),
        ];

        // No DNSKEY has arrived at all yet -- either this zone's very first transfer has not
        // completed, or it was only just marked signed and nothing has been pulled since. There
        // is nothing to serve; the caller leaves whatever this cache already held for this zone
        // untouched rather than replacing it with an empty, useless entry.
        if (dnsKeyEntities.Count == 0)
            return null;

        List<DnsResourceRecord> dnsKeyRecords =
        [
            .. dnsKeyEntities
                .Select(record =>
                    RecordEntityCodec.ToWireRecord(record, DnsName.Parse(record.Name))
                )
                .Where(record => record is not null),
        ];

        // Every RRSIG, grouped by the (owner name, covered type) pair of the RRset it actually
        // signs -- read directly off each record's own decoded TypeCovered field, exactly the
        // same key SignedZoneData.GetSignatures looks records up by. The single shared signature
        // expiration this whole pass tracks is the earliest one found among them, mirroring
        // ZoneSigningService's own "one shared expiration per pass" shape closely enough to be a
        // meaningful "this transferred content is due for a fresher pull soon" signal, even
        // though nothing here actually schedules a re-sign off of it the way it does for a
        // locally-signed zone.
        Dictionary<
            (DnsName Name, DnsResourceRecordTypeEnum Type),
            List<DnsResourceRecord>
        > rrsigsByCoveredRrset = [];
        DateTimeOffset? earliestSignatureExpiresAtUtc = null;

        foreach (
            RecordEntity record in activeRecords.Where(record =>
                record.Type == DnsResourceRecordTypeEnum.RrSig
            )
        )
        {
            if (RecordEntityCodec.DecodeTypedData(record) is not RrSigRecordData rrsigData)
                continue;

            DnsName owner = DnsName.Parse(record.Name);
            DnsResourceRecord wireRecord = RecordEntityCodec.ToWireRecord(record, owner);

            if (wireRecord is null)
                continue;

            (DnsName Owner, DnsResourceRecordTypeEnum TypeCovered) key = (
                owner,
                rrsigData.TypeCovered
            );

            if (!rrsigsByCoveredRrset.TryGetValue(key, out List<DnsResourceRecord> signatures))
                rrsigsByCoveredRrset[key] = signatures = [];

            signatures.Add(wireRecord);

            DateTimeOffset thisExpiration = DateTimeOffset.FromUnixTimeSeconds(
                rrsigData.SignatureExpiration
            );

            if (
                earliestSignatureExpiresAtUtc is null
                || thisExpiration < earliestSignatureExpiresAtUtc
            )
                earliestSignatureExpiresAtUtc = thisExpiration;
        }

        // The NSEC chain, re-sorted into canonical name order -- the same order
        // ZoneSigningService.BuildNsecChain originally produced it in, needed by
        // DenialOfExistenceProofBuilder's own descending "greatest record still <= target, wrap
        // to the end" walk to actually find the right covering record.
        List<DnsResourceRecord> nsecRecords =
        [
            .. activeRecords
                .Where(record => record.Type == DnsResourceRecordTypeEnum.Nsec)
                .Select(record =>
                    RecordEntityCodec.ToWireRecord(record, DnsName.Parse(record.Name))
                )
                .Where(record => record is not null)
                .OrderBy(
                    record => record.Name,
                    Comparer<DnsName>.Create(DnssecCanonicalization.CompareCanonicalOrder)
                ),
        ];

        // The NSEC3 chain, re-sorted into hash order -- the same order
        // ZoneSigningService.BuildNsec3Chain originally produced it in, computed by decoding each
        // record's own owner name back into its raw hash (Nsec3Hasher.ExtractHashFromOwnerName)
        // rather than recomputing the hash from scratch, since the owner name already carries it.
        List<DnsResourceRecord> nsec3Records =
        [
            .. activeRecords
                .Where(record => record.Type == DnsResourceRecordTypeEnum.Nsec3)
                .Select(record =>
                    RecordEntityCodec.ToWireRecord(record, DnsName.Parse(record.Name))
                )
                .Where(record => record is not null)
                .OrderBy(
                    record => Nsec3Hasher.ExtractHashFromOwnerName(record.Name),
                    ByteSequenceComparer.Instance
                ),
        ];

        // A zone uses exactly one denial-of-existence mode at a time, so at most one of these two
        // lists is ever non-empty -- whichever one is, becomes this zone's own chain.
        List<DnsResourceRecord> denialOfExistenceRecords =
            nsecRecords.Count > 0 ? nsecRecords : nsec3Records;

        RecordEntity nsec3ParamEntity = activeRecords.FirstOrDefault(record =>
            record.Type == DnsResourceRecordTypeEnum.Nsec3Param
        );
        DnsResourceRecord nsec3ParamRecord = nsec3ParamEntity is null
            ? null
            : RecordEntityCodec.ToWireRecord(nsec3ParamEntity, apexName);

        // Every DS record this zone itself carries for one of its own delegations -- generated by
        // whichever server actually signs this zone, transferred here exactly like any other
        // ordinary RRset, and grouped by delegation name the same way
        // SignedZoneData.GeneratedDsRecordsByDelegationName already expects.
        Dictionary<DnsName, IReadOnlyList<DnsResourceRecord>> generatedDsRecordsByDelegationName =
            activeRecords
                .Where(record => record.Type == DnsResourceRecordTypeEnum.DS)
                .GroupBy(record => DnsName.Parse(record.Name))
                .ToDictionary(
                    group => group.Key,
                    group =>
                        (IReadOnlyList<DnsResourceRecord>)
                            [
                                .. group
                                    .Select(record =>
                                        RecordEntityCodec.ToWireRecord(record, group.Key)
                                    )
                                    .Where(record => record is not null),
                            ]
                );

        return new SignedZoneData(
            dnsKeyRecords,
            rrsigsByCoveredRrset.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<DnsResourceRecord>)pair.Value
            ),
            denialOfExistenceRecords,
            nsec3ParamRecord,
            generatedDsRecordsByDelegationName,
            // A zone with a DNSKEY but genuinely no RRSIG at all would be a malformed transfer
            // that should never actually happen in practice -- "now" is a safe, conservative
            // fallback that reads as "already due for a fresher pull" rather than throwing.
            earliestSignatureExpiresAtUtc ?? DateTimeOffset.UtcNow
        );
    }

    /// <summary>
    ///     This class compares two byte sequences by unsigned value, most significant byte first
    ///     -- the same order <see cref="ZoneSigningService" /> sorts its own NSEC3 chain by, and
    ///     the order an NSEC3 owner name's own embedded hash must be re-sorted into here for the
    ///     same chain-walk logic to find the right covering record.
    /// </summary>
    private sealed class ByteSequenceComparer : IComparer<byte[]>
    {
        /// <summary>
        ///     This property contains the single shared instance of this stateless comparer.
        /// </summary>
        public static readonly ByteSequenceComparer Instance = new();

        /// <inheritdoc />
        public int Compare(byte[] x, byte[] y)
        {
            int commonLength = Math.Min(x.Length, y.Length);

            for (int i = 0; i < commonLength; i++)
                if (x[i] != y[i])
                    return x[i] - y[i];

            return x.Length - y.Length;
        }
    }
}
