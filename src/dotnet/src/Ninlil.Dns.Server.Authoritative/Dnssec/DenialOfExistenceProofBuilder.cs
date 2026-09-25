using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Server.Dnssec;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Dnssec;

/// <summary>
///     This class picks the NSEC or NSEC3 records, with their RRSIGs, that RFC 4035 §3.1.3 and
///     RFC 5155 §7.2 require with a negative answer, a wildcard answer or an insecure
///     referral. <see cref="Resolution.AuthoritativeQueryResolver" /> calls it at query time.
///     <para>
///         The records come from the chain <see cref="ZoneSigningService" /> already built
///         (<see cref="SignedZoneData.DenialOfExistenceRecords" />); nothing is signed per query.
///         For NSEC3, names not in the chain (such as the next closer name) are hashed at query
///         time to find the covering record. Lookups scan the chain linearly.
///     </para>
/// </summary>
public static class DenialOfExistenceProofBuilder
{
    /// <summary>
    ///     This method builds the NODATA proof (the name exists but lacks the type): the NSEC or
    ///     NSEC3 record for that name, with its RRSIG.
    /// </summary>
    /// <param name="signed">The zone's signed content.</param>
    /// <param name="zone">The zone's database record, for its NSEC3 hash parameters.</param>
    /// <param name="queryName">The name that exists but lacks the queried type.</param>
    /// <returns>The proof records, or an empty list if the matching record was not found.</returns>
    public static List<DnsResourceRecord> BuildNoDataProof(
        SignedZoneData signed,
        ZoneEntity zone,
        DnsName queryName
    ) => WithSignature(signed, FindExactMatch(signed, zone, queryName), CoveringType(zone));

    /// <summary>
    ///     This method builds the NXDOMAIN proof (RFC 4035 §3.1.3.2, RFC 5155 §7.2.2): that the
    ///     name does not exist (through its closest encloser and the next closer name) and that no
    ///     wildcard at the closest encloser could have matched.
    /// </summary>
    /// <param name="signed">The zone's signed content.</param>
    /// <param name="zone">The zone's database record.</param>
    /// <param name="apexName">The zone's apex name.</param>
    /// <param name="queryName">The name that does not exist.</param>
    /// <returns>The proof records, deduplicated.</returns>
    public static List<DnsResourceRecord> BuildNxDomainProof(
        SignedZoneData signed,
        ZoneEntity zone,
        DnsName apexName,
        DnsName queryName
    )
    {
        List<DnsResourceRecord> proof = BuildWildcardProof(signed, zone, apexName, queryName);
        (DnsName closestEncloser, _) = FindClosestEncloserAndNextCloser(
            signed,
            zone,
            apexName,
            queryName
        );
        DnsName wildcardName = WildcardNameFor(closestEncloser);

        proof.AddRange(BuildCoveringProof(signed, zone, apexName, wildcardName));

        return Deduplicate(proof);
    }

    /// <summary>
    ///     This method builds the proof that goes with a wildcard answer (RFC 4035 §3.1.3.3): that
    ///     no exact match exists for the queried name. Unlike <see cref="BuildNxDomainProof" />, it
    ///     does not deny the wildcard, since the wildcard matched.
    /// </summary>
    /// <param name="signed">The zone's signed content.</param>
    /// <param name="zone">The zone's database record.</param>
    /// <param name="apexName">The zone's apex name.</param>
    /// <param name="queryName">The originally queried name, before wildcard expansion.</param>
    /// <returns>The proof records, deduplicated.</returns>
    public static List<DnsResourceRecord> BuildWildcardProof(
        SignedZoneData signed,
        ZoneEntity zone,
        DnsName apexName,
        DnsName queryName
    ) => Deduplicate(BuildCoveringProof(signed, zone, apexName, queryName));

    /// <summary>
    ///     This method builds the proof that goes with an insecure referral (RFC 4035 §3.1.4): the
    ///     delegation point's NSEC or NSEC3 record, whose bitmap lacks DS, with its RRSIG. For a
    ///     secure delegation it returns an empty list, since the signed DS is sent instead.
    /// </summary>
    /// <param name="signed">The zone's signed content.</param>
    /// <param name="zone">The zone's database record.</param>
    /// <param name="delegationPoint">The delegation point's name.</param>
    /// <returns>The proof records, or an empty list for a secure delegation.</returns>
    public static List<DnsResourceRecord> BuildInsecureDelegationProof(
        SignedZoneData signed,
        ZoneEntity zone,
        DnsName delegationPoint
    )
    {
        if (signed.GeneratedDsRecordsByDelegationName.ContainsKey(delegationPoint))
            return [];

        return WithSignature(
            signed,
            FindExactMatch(signed, zone, delegationPoint),
            CoveringType(zone)
        );
    }

    /// <summary>
    ///     This method builds the "no exact match" part of a proof: for NSEC, the record covering
    ///     <paramref name="name" />; for NSEC3, the closest encloser proof (the record matching the
    ///     closest encloser's hash, plus the record covering the next closer name).
    /// </summary>
    private static List<DnsResourceRecord> BuildCoveringProof(
        SignedZoneData signed,
        ZoneEntity zone,
        DnsName apexName,
        DnsName name
    )
    {
        DnsResourceRecordTypeEnum coveringType = CoveringType(zone);

        if (zone.DenialOfExistenceMode != DnssecDenialOfExistenceModeEnum.Nsec3)
            return WithSignature(signed, FindCoveringNsec(signed, name), coveringType);

        (DnsName closestEncloser, DnsName nextCloser) = FindClosestEncloserAndNextCloser(
            signed,
            zone,
            apexName,
            name
        );

        List<DnsResourceRecord> proof = WithSignature(
            signed,
            FindExactMatch(signed, zone, closestEncloser),
            coveringType
        );
        proof.AddRange(
            WithSignature(signed, FindCoveringNsec3(signed, zone, nextCloser), coveringType)
        );

        return proof;
    }

    /// <summary>
    ///     This method finds the chain record for a name: by owner name for NSEC, by hash for NSEC3
    ///     (whose owner names are hashes).
    /// </summary>
    private static DnsResourceRecord FindExactMatch(
        SignedZoneData signed,
        ZoneEntity zone,
        DnsName name
    ) =>
        zone.DenialOfExistenceMode == DnssecDenialOfExistenceModeEnum.Nsec3
            ? FindExactNsec3(signed, zone, name)
            : signed.DenialOfExistenceRecords.FirstOrDefault(record => record.Name == name);

    /// <summary>
    ///     This method finds the NSEC record covering a name: the last record whose owner sorts at
    ///     or before <paramref name="name" /> in canonical order, or the chain's last record when
    ///     none does.
    /// </summary>
    private static DnsResourceRecord FindCoveringNsec(SignedZoneData signed, DnsName name)
    {
        IReadOnlyList<DnsResourceRecord> chain = signed.DenialOfExistenceRecords;

        for (int i = chain.Count - 1; i >= 0; i--)
            if (DnssecCanonicalization.CompareCanonicalOrder(chain[i].Name, name) <= 0)
                return chain[i];

        return chain.Count > 0 ? chain[^1] : null;
    }

    /// <summary>
    ///     This method finds the NSEC3 record covering a name's hash, with the same rule as
    ///     <see cref="FindCoveringNsec" /> but in hash order.
    /// </summary>
    private static DnsResourceRecord FindCoveringNsec3(
        SignedZoneData signed,
        ZoneEntity zone,
        DnsName name
    )
    {
        byte[] targetHash = HashFor(zone, name);
        IReadOnlyList<DnsResourceRecord> chain = signed.DenialOfExistenceRecords;

        for (int i = chain.Count - 1; i >= 0; i--)
            if (CompareBytes(Nsec3Hasher.ExtractHashFromOwnerName(chain[i].Name), targetHash) <= 0)
                return chain[i];

        return chain.Count > 0 ? chain[^1] : null;
    }

    /// <summary>
    ///     This method finds the NSEC3 record whose hash equals the name's hash.
    /// </summary>
    private static DnsResourceRecord FindExactNsec3(
        SignedZoneData signed,
        ZoneEntity zone,
        DnsName name
    )
    {
        DnsName ownerName = Nsec3Hasher.ToOwnerName(HashFor(zone, name), DnsName.Parse(zone.Name));
        return signed.DenialOfExistenceRecords.FirstOrDefault(record => record.Name == ownerName);
    }

    /// <summary>
    ///     This method computes a name's RFC 5155 §5 hash with the zone's iterations and salt.
    /// </summary>
    private static byte[] HashFor(ZoneEntity zone, DnsName name) =>
        Nsec3Hasher.ComputeHash(
            1,
            name,
            zone.Nsec3Iterations,
            string.IsNullOrEmpty(zone.Nsec3SaltHex) ? [] : Convert.FromHexString(zone.Nsec3SaltHex)
        );

    /// <summary>
    ///     This method finds the closest encloser (the nearest existing ancestor, or the apex) and
    ///     the next closer name (RFC 5155 §7.2.1: the name one label longer, on the way to
    ///     <paramref name="name" />, which does not exist). Existence is tested with
    ///     <see cref="FindExactMatch" />, so it works for NSEC3's hashed names too.
    /// </summary>
    private static (DnsName ClosestEncloser, DnsName NextCloser) FindClosestEncloserAndNextCloser(
        SignedZoneData signed,
        ZoneEntity zone,
        DnsName apexName,
        DnsName name
    )
    {
        DnsName candidate = name;
        DnsName previous = null;

        while (true)
        {
            if (candidate == apexName || FindExactMatch(signed, zone, candidate) is not null)
                return (candidate, previous ?? candidate);

            previous = candidate;
            candidate = new DnsName([.. candidate.Labels.Skip(1)]);
        }
    }

    /// <summary>
    ///     This method builds the wildcard name <c>*.encloser</c>.
    /// </summary>
    private static DnsName WildcardNameFor(DnsName encloser) =>
        new([
            [(byte)'*'],
            .. encloser.Labels,
        ]);

    /// <summary>
    ///     This method returns a chain record together with its RRSIGs, or an empty list when the
    ///     record was not found (which should not happen in a signed zone).
    /// </summary>
    private static List<DnsResourceRecord> WithSignature(
        SignedZoneData signed,
        DnsResourceRecord record,
        DnsResourceRecordTypeEnum recordType
    )
    {
        if (record is null)
            return [];

        List<DnsResourceRecord> result = [record];
        result.AddRange(signed.GetSignatures(record.Name, recordType));

        return result;
    }

    /// <summary>
    ///     This method removes repeated records from a proof, since the closest encloser proof and
    ///     the wildcard proof can pick the same record. The records come from the same chain, so
    ///     duplicates are the same instances.
    /// </summary>
    private static List<DnsResourceRecord> Deduplicate(IEnumerable<DnsResourceRecord> records) =>
        [.. records.Distinct()];

    /// <summary>
    ///     This method reports which record type a zone's denial-of-existence chain uses.
    /// </summary>
    private static DnsResourceRecordTypeEnum CoveringType(ZoneEntity zone) =>
        zone.DenialOfExistenceMode == DnssecDenialOfExistenceModeEnum.Nsec3
            ? DnsResourceRecordTypeEnum.Nsec3
            : DnsResourceRecordTypeEnum.Nsec;

    /// <summary>
    ///     This method compares byte sequences as unsigned values, most significant byte first, the
    ///     order <see cref="ZoneSigningService" /> sorts the NSEC3 chain in.
    /// </summary>
    private static int CompareBytes(byte[] x, byte[] y)
    {
        int commonLength = Math.Min(x.Length, y.Length);

        for (int i = 0; i < commonLength; i++)
            if (x[i] != y[i])
                return x[i] - y[i];

        return x.Length - y.Length;
    }
}
