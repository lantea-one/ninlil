using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Dnssec;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Validation;

/// <summary>
///     This class checks authenticated denial of existence (RFC 4035 §5.4, RFC 5155 §8): that
///     NSEC or NSEC3 records prove a name does not exist, or exists without the queried type.
///     <see cref="DnssecValidator" /> uses it for negative answers and for proving a delegation
///     has no DS.
///     <para>
///         It checks only the structure (canonical order and hash ranges). The caller must verify
///         with <see cref="RrsetSignatureVerifier" /> that every NSEC/NSEC3 record is signed by a
///         trusted key, or a forged record could prove anything.
///     </para>
/// </summary>
public static class DenialOfExistenceValidator
{
    /// <summary>
    ///     This method checks an NSEC NODATA proof: an NSEC owned by the name whose type bitmap
    ///     lacks the queried type. The CNAME bit is not checked, and wildcard NODATA is not
    ///     handled.
    /// </summary>
    public static bool VerifyNoData(
        DnsName queriedName,
        DnsResourceRecordTypeEnum queriedType,
        IReadOnlyList<DnsResourceRecord> dnssecRecords
    ) =>
        DecodeNsecs(dnssecRecords)
            .Any(entry => entry.Owner == queriedName && !entry.Data.Types.Contains(queriedType));

    /// <summary>
    ///     This method checks an NSEC NXDOMAIN proof: an NSEC covering the name, and an NSEC
    ///     covering the wildcard at the closest encloser. The closest encloser is taken as the
    ///     nearest ancestor that owns one of the NSEC records.
    /// </summary>
    public static bool VerifyNxDomain(
        DnsName queriedName,
        IReadOnlyList<DnsResourceRecord> dnssecRecords
    )
    {
        List<(DnsName Owner, NsecRecordData Data)> nsecs = DecodeNsecs(dnssecRecords);

        if (FindCoveringNsec(queriedName, nsecs) is null)
            return false;

        DnsName closestEncloser = FindClosestEncloser(queriedName, nsecs);

        if (closestEncloser is null)
            return false;

        DnsName wildcard = DnsName.Parse($"*.{closestEncloser}");

        return FindCoveringNsec(wildcard, nsecs) is not null;
    }

    /// <summary>
    ///     This method checks an NSEC3 NODATA proof: an NSEC3 whose hash matches the name and whose
    ///     bitmap lacks the queried type. It serves ordinary NODATA answers and the missing-DS
    ///     proof (RFC 5155).
    /// </summary>
    public static bool VerifyNoDataNsec3(
        DnsName queriedName,
        DnsResourceRecordTypeEnum queriedType,
        IReadOnlyList<DnsResourceRecord> dnssecRecords
    )
    {
        List<(DnsName Owner, Nsec3RecordData Data)> nsec3s = DecodeNsec3s(dnssecRecords);

        foreach ((DnsName owner, Nsec3RecordData data) in nsec3s)
        {
            byte[] hash = Nsec3Hasher.ComputeHash(
                data.HashAlgorithm,
                queriedName,
                data.Iterations,
                data.Salt
            );

            if (Nsec3Hasher.ExtractHashFromOwnerName(owner).AsSpan().SequenceEqual(hash))
                return !data.Types.Contains(queriedType);
        }

        return false;
    }

    /// <summary>
    ///     This method checks an NSEC3 opt-out proof of an unsigned delegation: an NSEC3 with the
    ///     Opt-Out flag whose range covers the delegation name's hash. It is used when the
    ///     zone has no NSEC3 for the name itself. The name's own hash is tested, without a closest
    ///     encloser proof.
    /// </summary>
    public static bool VerifyOptOut(
        DnsName delegationName,
        IReadOnlyList<DnsResourceRecord> dnssecRecords
    )
    {
        List<(DnsName Owner, Nsec3RecordData Data)> nsec3s = DecodeNsec3s(dnssecRecords);

        if (nsec3s.Count == 0)
            return false;

        Nsec3RecordData parameters = nsec3s[0].Data;
        byte[] targetHash = Nsec3Hasher.ComputeHash(
            parameters.HashAlgorithm,
            delegationName,
            parameters.Iterations,
            parameters.Salt
        );

        return nsec3s.Any(entry => entry.Data.IsOptOut && CoversHash(entry, targetHash));
    }

    /// <summary>
    ///     This method checks an NSEC3 NXDOMAIN proof (the RFC 5155 §8.3 closest-encloser proof):
    ///     an NSEC3 matching the closest encloser, one covering the next closer name, and one
    ///     covering the wildcard at the closest encloser. All hashes use the first record's
    ///     parameters.
    /// </summary>
    public static bool VerifyNxDomainNsec3(
        DnsName queriedName,
        IReadOnlyList<DnsResourceRecord> dnssecRecords
    )
    {
        List<(DnsName Owner, Nsec3RecordData Data)> nsec3s = DecodeNsec3s(dnssecRecords);

        if (nsec3s.Count == 0)
            return false;

        Nsec3RecordData parameters = nsec3s[0].Data;
        DnsName closestEncloser = FindClosestEncloserNsec3(queriedName, nsec3s, parameters);

        if (closestEncloser is null || closestEncloser.Labels.Count >= queriedName.Labels.Count)
            return false;

        DnsName nextCloser = new([
            .. queriedName.Labels.Skip(queriedName.Labels.Count - closestEncloser.Labels.Count - 1),
        ]);
        byte[] nextCloserHash = Nsec3Hasher.ComputeHash(
            parameters.HashAlgorithm,
            nextCloser,
            parameters.Iterations,
            parameters.Salt
        );

        if (!nsec3s.Any(entry => CoversHash(entry, nextCloserHash)))
            return false;

        DnsName wildcard = DnsName.Parse($"*.{closestEncloser}");
        byte[] wildcardHash = Nsec3Hasher.ComputeHash(
            parameters.HashAlgorithm,
            wildcard,
            parameters.Iterations,
            parameters.Salt
        );

        return nsec3s.Any(entry => CoversHash(entry, wildcardHash));
    }

    /// <summary>
    ///     This method finds an NSEC record whose owner-to-next range covers a name, including the
    ///     last NSEC in the zone, whose range wraps around to the apex.
    /// </summary>
    private static (DnsName Owner, NsecRecordData Data)? FindCoveringNsec(
        DnsName name,
        List<(DnsName Owner, NsecRecordData Data)> nsecs
    )
    {
        foreach ((DnsName owner, NsecRecordData data) in nsecs)
        {
            int ownerToName = DnssecCanonicalization.CompareCanonicalOrder(owner, name);
            int nameToNext = DnssecCanonicalization.CompareCanonicalOrder(
                name,
                data.NextDomainName
            );
            int ownerToNext = DnssecCanonicalization.CompareCanonicalOrder(
                owner,
                data.NextDomainName
            );

            // Normally owner < next and coverage is owner < name < next. The zone's last NSEC
            // wraps to the apex (owner >= next) and covers everything after the owner or before
            // the next name.
            bool wrapsAroundZone = ownerToNext >= 0;
            bool coveredNormally = ownerToName < 0 && nameToNext < 0;
            bool coveredByWrap = wrapsAroundZone && (ownerToName < 0 || nameToNext < 0);

            if (coveredNormally || coveredByWrap)
                return (owner, data);
        }

        return null;
    }

    /// <summary>
    ///     This method finds the longest proper ancestor of a name that owns one of the NSEC
    ///     records, used as the closest encloser for the wildcard check.
    /// </summary>
    private static DnsName FindClosestEncloser(
        DnsName name,
        List<(DnsName Owner, NsecRecordData Data)> nsecs
    )
    {
        for (int skip = 1; skip <= name.Labels.Count; skip++)
        {
            DnsName candidate = new([.. name.Labels.Skip(skip)]);

            if (nsecs.Any(entry => entry.Owner == candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>
    ///     This method finds the longest proper ancestor of a name whose hash matches an NSEC3
    ///     owner: the closest encloser of RFC 5155 §8.3.
    /// </summary>
    private static DnsName FindClosestEncloserNsec3(
        DnsName name,
        List<(DnsName Owner, Nsec3RecordData Data)> nsec3s,
        Nsec3RecordData parameters
    )
    {
        for (int skip = 1; skip <= name.Labels.Count; skip++)
        {
            DnsName candidate = new([.. name.Labels.Skip(skip)]);
            byte[] hash = Nsec3Hasher.ComputeHash(
                parameters.HashAlgorithm,
                candidate,
                parameters.Iterations,
                parameters.Salt
            );

            if (
                nsec3s.Any(entry =>
                    Nsec3Hasher.ExtractHashFromOwnerName(entry.Owner).AsSpan().SequenceEqual(hash)
                )
            )
                return candidate;
        }

        return null;
    }

    /// <summary>
    ///     This method checks whether an NSEC3 record's hash range, including the wraparound case,
    ///     covers a target hash.
    /// </summary>
    private static bool CoversHash((DnsName Owner, Nsec3RecordData Data) entry, byte[] targetHash)
    {
        byte[] ownerHash = Nsec3Hasher.ExtractHashFromOwnerName(entry.Owner);
        byte[] nextHash = entry.Data.NextHashedOwnerName;

        int ownerToTarget = CompareUnsigned(ownerHash, targetHash);
        int targetToNext = CompareUnsigned(targetHash, nextHash);
        int ownerToNext = CompareUnsigned(ownerHash, nextHash);

        // The same wraparound rule as FindCoveringNsec, applied to hashes.
        bool wrapsAroundZone = ownerToNext >= 0;
        bool coveredNormally = ownerToTarget < 0 && targetToNext < 0;
        bool coveredByWrap = wrapsAroundZone && (ownerToTarget < 0 || targetToNext < 0);

        return coveredNormally || coveredByWrap;
    }

    /// <summary>
    ///     This method compares two byte sequences as unsigned, left-justified octet strings, the
    ///     order NSEC3 hashes follow.
    /// </summary>
    private static int CompareUnsigned(byte[] left, byte[] right)
    {
        int commonLength = Math.Min(left.Length, right.Length);

        for (int i = 0; i < commonLength; i++)
            if (left[i] != right[i])
                return left[i] - right[i];

        return left.Length - right.Length;
    }

    /// <summary>
    ///     This method decodes every NSEC record in a set of DNSSEC records.
    /// </summary>
    private static List<(DnsName Owner, NsecRecordData Data)> DecodeNsecs(
        IReadOnlyList<DnsResourceRecord> records
    ) =>
        [
            .. records
                .Where(record => record.Type == DnsResourceRecordTypeEnum.Nsec)
                .Select(record =>
                    (
                        record.Name,
                        Data: DnsRecordDataCodecRegistry.Decode(
                            DnsResourceRecordTypeEnum.Nsec,
                            record.Data
                        ) as NsecRecordData
                    )
                )
                .Where(pair => pair.Data is not null)
                .Select(pair => (pair.Name, pair.Data)),
        ];

    /// <summary>
    ///     This method decodes every NSEC3 record in a set of DNSSEC records.
    /// </summary>
    private static List<(DnsName Owner, Nsec3RecordData Data)> DecodeNsec3s(
        IReadOnlyList<DnsResourceRecord> records
    ) =>
        [
            .. records
                .Where(record => record.Type == DnsResourceRecordTypeEnum.Nsec3)
                .Select(record =>
                    (
                        record.Name,
                        Data: DnsRecordDataCodecRegistry.Decode(
                            DnsResourceRecordTypeEnum.Nsec3,
                            record.Data
                        ) as Nsec3RecordData
                    )
                )
                .Where(pair => pair.Data is not null)
                .Select(pair => (pair.Name, pair.Data)),
        ];
}
