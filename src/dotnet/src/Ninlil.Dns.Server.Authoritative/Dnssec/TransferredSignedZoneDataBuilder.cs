using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Dnssec;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Dnssec;

/// <summary>
///     This class builds a secondary zone's <see cref="SignedZoneData" /> from the RRSIG, NSEC,
///     NSEC3, NSEC3PARAM, DNSKEY and DS <see cref="RecordEntity" /> rows a zone transfer stored:
///     the counterpart of <see cref="ZoneSigningService" /> for zones this server does not sign.
///     It uses no keys and computes no signatures. The records are served as received; they
///     are protected only by the transfer's TSIG, and their signatures are not verified here.
///     <para>
///         Storage does not keep the transfer's record order, so the NSEC chain is re-sorted into
///         canonical order and the NSEC3 chain into hash order, as
///         <see cref="DenialOfExistenceProofBuilder" /> expects, and RRSIGs are grouped by the
///         RRset they cover.
///     </para>
/// </summary>
public static class TransferredSignedZoneDataBuilder
{
    /// <summary>
    ///     This method builds a zone's <see cref="SignedZoneData" /> from its stored records.
    /// </summary>
    /// <param name="apexName">The zone's apex name.</param>
    /// <param name="storedRecords">Every record currently stored for the zone.</param>
    /// <returns>
    ///     The zone's transferred signed content, or <see langword="null" /> when no enabled DNSKEY
    ///     record is stored (usually before the first transfer completes).
    /// </returns>
    public static SignedZoneData Build(DnsName apexName, IReadOnlyList<RecordEntity> storedRecords)
    {
        // Disabled records are not served, so leave them out, as ZoneSigningService does.
        List<RecordEntity> activeRecords = [.. storedRecords.Where(record => !record.IsDisabled)];

        List<RecordEntity> dnsKeyEntities =
        [
            .. activeRecords.Where(record => record.Type == DnsResourceRecordTypeEnum.DnsKey),
        ];

        // No DNSKEY yet: nothing to serve. The caller keeps whatever it had cached.
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

        // Group RRSIGs by (owner name, TypeCovered), the key SignedZoneData.GetSignatures uses.
        // The earliest expiration becomes SignatureExpiresAtUtc; nothing re-signs a secondary
        // zone from it.
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

        // The NSEC chain in canonical order, which DenialOfExistenceProofBuilder's covering
        // search needs.
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

        // The NSEC3 chain in hash order, taking each hash from the owner name
        // (Nsec3Hasher.ExtractHashFromOwnerName).
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

        // A zone uses one mode, so at most one list is non-empty; NSEC wins if both are.
        List<DnsResourceRecord> denialOfExistenceRecords =
            nsecRecords.Count > 0 ? nsecRecords : nsec3Records;

        RecordEntity nsec3ParamEntity = activeRecords.FirstOrDefault(record =>
            record.Type == DnsResourceRecordTypeEnum.Nsec3Param
        );
        DnsResourceRecord nsec3ParamRecord = nsec3ParamEntity is null
            ? null
            : RecordEntityCodec.ToWireRecord(nsec3ParamEntity, apexName);

        // DS records for the zone's delegations, as transferred, grouped by delegation name
        // for SignedZoneData.GeneratedDsRecordsByDelegationName.
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
            // A DNSKEY with no RRSIGs means a malformed transfer; use "now" rather than throw.
            earliestSignatureExpiresAtUtc ?? DateTimeOffset.UtcNow
        );
    }

    /// <summary>
    ///     This class compares byte sequences as unsigned values, most significant byte first, the
    ///     order of the NSEC3 chain.
    /// </summary>
    private sealed class ByteSequenceComparer : IComparer<byte[]>
    {
        /// <summary>
        ///     This field contains the shared instance.
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
