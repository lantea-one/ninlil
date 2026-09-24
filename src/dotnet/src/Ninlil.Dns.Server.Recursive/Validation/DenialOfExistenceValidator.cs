using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Dnssec;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Validation;

/// <summary>
///     This class implements RFC 4035 §5.4 / RFC 5155 §8's authenticated denial of existence:
///     proving a name doesn't exist, or exists but lacks a queried type, from a signed NSEC or
///     NSEC3 chain -- rather than simply trusting an unsigned NXDOMAIN/NODATA response, which is
///     exactly the gap that makes a plain (non-validating) resolver unable to detect a forged
///     negative answer.
///     <para>
///         This class only performs the structural (canonical-ordering/hash-range) half of the
///         proof; whoever calls it is responsible for separately verifying, via
///         <see cref="RrsetSignatureVerifier" />, that every NSEC/NSEC3 record involved is itself
///         validly signed by an already-trusted key -- an unsigned or wrongly-signed NSEC/NSEC3
///         record would otherwise "prove" anything an attacker wanted.
///     </para>
/// </summary>
public static class DenialOfExistenceValidator
{
    /// <summary>
    ///     This method proves NODATA (the name exists, but not with the queried type) using
    ///     classic NSEC.
    /// </summary>
    public static bool VerifyNoData(
        DnsName queriedName,
        DnsResourceRecordTypeEnum queriedType,
        IReadOnlyList<DnsResourceRecord> dnssecRecords
    ) =>
        DecodeNsecs(dnssecRecords)
            .Any(entry => entry.Owner == queriedName && !entry.Data.Types.Contains(queriedType));

    /// <summary>
    ///     This method proves NXDOMAIN (the name does not exist, nor could a wildcard have
    ///     synthesized an answer for it) using classic NSEC.
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
    ///     This method proves NODATA for a DS query specifically (the exact-match form of RFC
    ///     5155 §8.1's proof that an insecure delegation has no DS) using NSEC3.
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
    ///     This method proves an insecure delegation via RFC 5155 §5.2's opt-out mechanism: an
    ///     NSEC3 record whose hash range *covers* (rather than exactly matches) the delegation
    ///     name's own hash, with its Opt-Out bit set -- used when the exact-match proof
    ///     (<see cref="VerifyNoDataNsec3" />) fails because the zone never published an NSEC3
    ///     record for this exact name at all.
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
    ///     This method proves NXDOMAIN using NSEC3's RFC 5155 §8.3 closest-encloser proof: the
    ///     closest existing ancestor, the nonexistence of the next-closer name toward the query,
    ///     and the nonexistence of a wildcard at the closest encloser, all three covered by the
    ///     provided NSEC3 records.
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
    ///     This method finds an NSEC record whose owner/next-domain-name range covers a name --
    ///     including the wraparound case where the covering record is the last one in the zone's
    ///     own canonical order.
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

            // An owner name's NextDomainName is normally strictly greater than the owner itself
            // in canonical order; the one exception is the zone's very last NSEC record, whose
            // own "next" wraps back around to the zone apex (so owner >= next). Ordinary
            // coverage just needs owner < name < next; a wrapping record instead covers
            // everything past the owner OR everything before the next, since the covered range
            // spans the end of the canonical ordering and back to the start.
            bool wrapsAroundZone = ownerToNext >= 0;
            bool coveredNormally = ownerToName < 0 && nameToNext < 0;
            bool coveredByWrap = wrapsAroundZone && (ownerToName < 0 || nameToNext < 0);

            if (coveredNormally || coveredByWrap)
                return (owner, data);
        }

        return null;
    }

    /// <summary>
    ///     This method finds the longest ancestor of a name that some provided NSEC record's own
    ///     owner name exactly matches -- the closest encloser RFC 4035 §5.4 needs to also prove no
    ///     wildcard could have matched.
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
    ///     This method finds the longest ancestor of a name whose hash some provided NSEC3
    ///     record's own owner name exactly matches -- RFC 5155 §8.3's closest encloser.
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
    ///     This method checks whether an NSEC3 record's own hash range -- including the
    ///     wraparound case -- covers a target hash.
    /// </summary>
    private static bool CoversHash((DnsName Owner, Nsec3RecordData Data) entry, byte[] targetHash)
    {
        byte[] ownerHash = Nsec3Hasher.ExtractHashFromOwnerName(entry.Owner);
        byte[] nextHash = entry.Data.NextHashedOwnerName;

        int ownerToTarget = CompareUnsigned(ownerHash, targetHash);
        int targetToNext = CompareUnsigned(targetHash, nextHash);
        int ownerToNext = CompareUnsigned(ownerHash, nextHash);

        // Same wraparound reasoning as FindCoveringNsec's own hash-range check, just applied to
        // NSEC3's hashed owner/next-hashed-owner range instead of a plain NSEC next-domain-name.
        bool wrapsAroundZone = ownerToNext >= 0;
        bool coveredNormally = ownerToTarget < 0 && targetToNext < 0;
        bool coveredByWrap = wrapsAroundZone && (ownerToTarget < 0 || targetToNext < 0);

        return coveredNormally || coveredByWrap;
    }

    /// <summary>
    ///     This method compares two byte sequences as an unsigned, left-justified octet string --
    ///     the same rule RFC 5155 §7 orders NSEC3 hashes by.
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
