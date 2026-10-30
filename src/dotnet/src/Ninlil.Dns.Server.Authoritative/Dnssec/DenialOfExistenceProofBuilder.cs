using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
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
///         The chain's own kind and hash parameters decide how records are found
///         (<see cref="SignedZoneData.DenialOfExistenceMode" /> and
///         <see cref="SignedZoneData.Nsec3Parameters" />), never the local zone row, because a
///         secondary's row does not carry the primary's NSEC3 settings. For NSEC3, names not in
///         the chain (such as the next closer name) are hashed at query time to find the covering
///         record; a chain with an unsupported hash algorithm gives empty proofs. Lookups scan the
///         chain linearly.
///     </para>
/// </summary>
public static class DenialOfExistenceProofBuilder
{
    /// <summary>
    ///     This method builds the NODATA proof (the name exists but lacks the type): the NSEC or
    ///     NSEC3 record for that name, with its RRSIG.
    /// </summary>
    /// <param name="signed">The zone's signed content.</param>
    /// <param name="apexName">The zone's apex name.</param>
    /// <param name="queryName">The name that exists but lacks the queried type.</param>
    /// <returns>
    ///     The proof records, or an empty list if the matching record was not found or the chain's
    ///     hash algorithm is unsupported.
    /// </returns>
    public static List<DnsResourceRecord> BuildNoDataProof(
        SignedZoneData signed,
        DnsName apexName,
        DnsName queryName
    ) =>
        CanProve(signed)
            ? WithSignature(
                signed,
                FindExactMatch(signed, apexName, queryName),
                CoveringType(signed)
            )
            : [];

    /// <summary>
    ///     This method builds the NXDOMAIN proof (RFC 4035 §3.1.3.2, RFC 5155 §7.2.2): that the
    ///     name does not exist (through its closest encloser and the next closer name) and that no
    ///     wildcard at the closest encloser could have matched.
    /// </summary>
    /// <param name="signed">The zone's signed content.</param>
    /// <param name="apexName">The zone's apex name.</param>
    /// <param name="queryName">The name that does not exist.</param>
    /// <returns>The proof records, deduplicated.</returns>
    public static List<DnsResourceRecord> BuildNxDomainProof(
        SignedZoneData signed,
        DnsName apexName,
        DnsName queryName
    )
    {
        if (!CanProve(signed))
            return [];

        List<DnsResourceRecord> proof = BuildWildcardProof(signed, apexName, queryName);
        (DnsName closestEncloser, _) = FindClosestEncloserAndNextCloser(
            signed,
            apexName,
            queryName
        );
        DnsName wildcardName = WildcardNameFor(closestEncloser);

        proof.AddRange(BuildCoveringProof(signed, apexName, wildcardName));

        return Deduplicate(proof);
    }

    /// <summary>
    ///     This method builds the proof that goes with a wildcard answer (RFC 4035 §3.1.3.3): that
    ///     no exact match exists for the queried name. Unlike <see cref="BuildNxDomainProof" />, it
    ///     does not deny the wildcard, since the wildcard matched.
    /// </summary>
    /// <param name="signed">The zone's signed content.</param>
    /// <param name="apexName">The zone's apex name.</param>
    /// <param name="queryName">The originally queried name, before wildcard expansion.</param>
    /// <returns>The proof records, deduplicated.</returns>
    public static List<DnsResourceRecord> BuildWildcardProof(
        SignedZoneData signed,
        DnsName apexName,
        DnsName queryName
    ) => CanProve(signed) ? Deduplicate(BuildCoveringProof(signed, apexName, queryName)) : [];

    /// <summary>
    ///     This method builds the proof for a wildcard NODATA answer: the name does not exist, but
    ///     the wildcard <c>*.closestEncloser</c> does and lacks the queried type (RFC 4035
    ///     §3.1.3.4, RFC 5155 §7.2.5). It holds the "no exact match" proof for the name plus the
    ///     chain record matching the wildcard, whose bitmap shows the type and CNAME absent.
    /// </summary>
    /// <param name="signed">The zone's signed content.</param>
    /// <param name="apexName">The zone's apex name.</param>
    /// <param name="queryName">The name that does not exist.</param>
    /// <param name="closestEncloser">The closest encloser of the name, whose wildcard matched.</param>
    /// <returns>The proof records, deduplicated.</returns>
    public static List<DnsResourceRecord> BuildWildcardNoDataProof(
        SignedZoneData signed,
        DnsName apexName,
        DnsName queryName,
        DnsName closestEncloser
    )
    {
        if (!CanProve(signed))
            return [];

        List<DnsResourceRecord> proof = BuildCoveringProof(signed, apexName, queryName);
        proof.AddRange(
            WithSignature(
                signed,
                FindExactMatch(signed, apexName, WildcardNameFor(closestEncloser)),
                CoveringType(signed)
            )
        );

        return Deduplicate(proof);
    }

    /// <summary>
    ///     This method builds the proof that goes with an insecure referral (RFC 4035 §3.1.4): the
    ///     delegation point's NSEC or NSEC3 record, whose bitmap lacks DS, with its RRSIG. For a
    ///     secure delegation it returns an empty list, since the signed DS is sent instead.
    /// </summary>
    /// <param name="signed">The zone's signed content.</param>
    /// <param name="apexName">The zone's apex name.</param>
    /// <param name="delegationPoint">The delegation point's name.</param>
    /// <returns>The proof records, or an empty list for a secure delegation.</returns>
    public static List<DnsResourceRecord> BuildInsecureDelegationProof(
        SignedZoneData signed,
        DnsName apexName,
        DnsName delegationPoint
    )
    {
        if (!CanProve(signed) || signed.DsRecordsByDelegationName.ContainsKey(delegationPoint))
            return [];

        return WithSignature(
            signed,
            FindExactMatch(signed, apexName, delegationPoint),
            CoveringType(signed)
        );
    }

    /// <summary>
    ///     This method builds the "no exact match" part of a proof: for NSEC, the record covering
    ///     <paramref name="name" />; for NSEC3, the closest encloser proof (the record matching the
    ///     closest encloser's hash, plus the record covering the next closer name).
    /// </summary>
    private static List<DnsResourceRecord> BuildCoveringProof(
        SignedZoneData signed,
        DnsName apexName,
        DnsName name
    )
    {
        DnsResourceRecordTypeEnum coveringType = CoveringType(signed);

        if (signed.DenialOfExistenceMode != DnssecDenialOfExistenceModeEnum.Nsec3)
            return WithSignature(signed, FindCoveringNsec(signed, name), coveringType);

        (DnsName closestEncloser, DnsName nextCloser) = FindClosestEncloserAndNextCloser(
            signed,
            apexName,
            name
        );

        List<DnsResourceRecord> proof = WithSignature(
            signed,
            FindExactMatch(signed, apexName, closestEncloser),
            coveringType
        );
        proof.AddRange(WithSignature(signed, FindCoveringNsec3(signed, nextCloser), coveringType));

        return proof;
    }

    /// <summary>
    ///     This method finds the chain record for a name: by owner name for NSEC, by hash for NSEC3
    ///     (whose owner names are hashes).
    /// </summary>
    private static DnsResourceRecord FindExactMatch(
        SignedZoneData signed,
        DnsName apexName,
        DnsName name
    ) =>
        signed.DenialOfExistenceMode == DnssecDenialOfExistenceModeEnum.Nsec3
            ? FindExactNsec3(signed, apexName, name)
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
    private static DnsResourceRecord FindCoveringNsec3(SignedZoneData signed, DnsName name)
    {
        byte[] targetHash = HashFor(signed, name);
        IReadOnlyList<DnsResourceRecord> chain = signed.DenialOfExistenceRecords;

        // A record whose owner label is not base32hex has no hash to compare, so it covers nothing.
        DnsResourceRecord last = null;

        for (int i = chain.Count - 1; i >= 0; i--)
        {
            if (!Nsec3Hasher.TryExtractHashFromOwnerName(chain[i].Name, out byte[] ownerHash))
                continue;

            last ??= chain[i];

            if (CompareBytes(ownerHash, targetHash) <= 0)
                return chain[i];
        }

        // The target sorts before every hash, so the last record in hash order wraps around to it.
        return last;
    }

    /// <summary>
    ///     This method finds the NSEC3 record whose hash equals the name's hash.
    /// </summary>
    private static DnsResourceRecord FindExactNsec3(
        SignedZoneData signed,
        DnsName apexName,
        DnsName name
    )
    {
        DnsName ownerName = Nsec3Hasher.ToOwnerName(HashFor(signed, name), apexName);
        return signed.DenialOfExistenceRecords.FirstOrDefault(record => record.Name == ownerName);
    }

    /// <summary>
    ///     This method computes a name's RFC 5155 §5 hash with the chain's own algorithm,
    ///     iterations and salt (<see cref="SignedZoneData.Nsec3Parameters" />). Callers check
    ///     <see cref="CanProve" /> first, so the algorithm is supported.
    /// </summary>
    private static byte[] HashFor(SignedZoneData signed, DnsName name) =>
        Nsec3Hasher.ComputeHash(
            signed.Nsec3Parameters.HashAlgorithm,
            name,
            signed.Nsec3Parameters.Iterations,
            signed.Nsec3Parameters.Salt
        );

    /// <summary>
    ///     This method reports whether proofs can be built from the chain. An NSEC chain always
    ///     can. An NSEC3 chain needs parameters with a hash algorithm
    ///     <see cref="Nsec3Hasher.IsSupported" /> knows; otherwise names cannot be hashed and the
    ///     proof is left empty instead of failing the query.
    /// </summary>
    private static bool CanProve(SignedZoneData signed) =>
        signed.DenialOfExistenceMode != DnssecDenialOfExistenceModeEnum.Nsec3
        || (
            signed.Nsec3Parameters is not null
            && Nsec3Hasher.IsSupported(signed.Nsec3Parameters.HashAlgorithm)
        );

    /// <summary>
    ///     This method finds the closest encloser (the nearest existing ancestor, or the apex) and
    ///     the next closer name (RFC 5155 §1.3: the name one label longer, on the way to
    ///     <paramref name="name" />, which does not exist). Existence is tested with
    ///     <see cref="FindExactMatch" />, so it works for NSEC3's hashed names too.
    /// </summary>
    private static (DnsName ClosestEncloser, DnsName NextCloser) FindClosestEncloserAndNextCloser(
        SignedZoneData signed,
        DnsName apexName,
        DnsName name
    )
    {
        DnsName candidate = name;
        DnsName previous = null;

        while (true)
        {
            if (candidate == apexName || FindExactMatch(signed, apexName, candidate) is not null)
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
    private static DnsResourceRecordTypeEnum CoveringType(SignedZoneData signed) =>
        signed.DenialOfExistenceMode == DnssecDenialOfExistenceModeEnum.Nsec3
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
