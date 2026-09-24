using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Server.Dnssec;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Dnssec;

/// <summary>
///     This class builds the NSEC/NSEC3 records (plus their own covering RRSIGs) RFC 4035 §3.1.3
///     and RFC 5155 §7.2 require alongside a negative response, a wildcard-synthesized positive
///     answer, or an insecure delegation referral -- the query-time counterpart to
///     <see cref="ZoneSigningService" />'s offline chain construction.
///     <para>
///         Every proof here is built from whichever chain <see cref="SignedZoneData.DenialOfExistenceRecords" />
///         already holds; nothing is computed or signed live, matching this project's own
///         deliberate stance against ever signing per query. NSEC3's own salted, iterated hash still has to be computed for
///         a name that is *not* already in the chain -- the closest encloser's synthetic wildcard
///         name, for example -- but that is a pure, deterministic hash lookup, not a signature.
///     </para>
/// </summary>
public static class DenialOfExistenceProofBuilder
{
    /// <summary>
    ///     This method builds the proof that an existing name lacks the queried type (NODATA):
    ///     the single NSEC or NSEC3 record whose own owner name matches exactly, plus its RRSIG.
    /// </summary>
    /// <param name="signed">The zone's signed content.</param>
    /// <param name="zone">The zone's own database record, for its NSEC3 hash parameters.</param>
    /// <param name="queryName">The name that exists but lacks the queried type.</param>
    /// <returns>The proof records, or an empty list if the matching record was not found.</returns>
    public static List<DnsResourceRecord> BuildNoDataProof(
        SignedZoneData signed,
        ZoneEntity zone,
        DnsName queryName
    ) => WithSignature(signed, FindExactMatch(signed, zone, queryName), CoveringType(zone));

    /// <summary>
    ///     This method builds the full non-existence proof RFC 4035 §3.1.3.2/RFC 5155 §7.2.2
    ///     require for an NXDOMAIN response: proof that the queried name itself does not exist
    ///     (via its closest encloser and the "next closer" name below it), and proof that no
    ///     wildcard beneath that closest encloser could have matched either.
    /// </summary>
    /// <param name="signed">The zone's signed content.</param>
    /// <param name="zone">The zone's own database record.</param>
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
    ///     This method builds the proof RFC 4035 §3.1.3.3 requires alongside a wildcard-synthesized
    ///     positive answer: that no exact (non-wildcard) match exists for the originally queried
    ///     name. Unlike <see cref="BuildNxDomainProof" />, this does *not* also need to disprove a
    ///     wildcard's existence, since a wildcard is exactly what matched.
    /// </summary>
    /// <param name="signed">The zone's signed content.</param>
    /// <param name="zone">The zone's own database record.</param>
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
    ///     This method builds the proof RFC 4035 §3.1.4 requires alongside an *insecure*
    ///     delegation referral: the delegation point's own NSEC/NSEC3 record (whose type bitmap
    ///     lacks DS), plus its RRSIG. Returns an empty list for a *secure* delegation, where the
    ///     DS RRset's own RRSIG is already proof enough and RFC 4035 does not require this too.
    /// </summary>
    /// <param name="signed">The zone's signed content.</param>
    /// <param name="zone">The zone's own database record.</param>
    /// <param name="delegationPoint">The delegation's own owner name.</param>
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
    ///     This method builds the "no exact/closer match" half of a proof: for NSEC, the single
    ///     record covering <paramref name="name" /> directly; for NSEC3, the closest encloser
    ///     proof (a match on the closest encloser's own hash, plus a covering record for the
    ///     "next closer" name one label below it).
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
    ///     This method finds the record whose owner name matches a name exactly -- for NSEC, a
    ///     direct name comparison; for NSEC3, a hash comparison, since the record's own owner
    ///     name is the hashed form, not <paramref name="name" /> itself.
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
    ///     This method finds the NSEC record covering a name in canonical order: the record whose
    ///     own owner name is the greatest one that still sorts at or before <paramref name="name" />,
    ///     wrapping to the chain's last record when nothing does.
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
    ///     This method finds the NSEC3 record covering a hash, the same "wraps to the last
    ///     record" rule as <see cref="FindCoveringNsec" /> but ordered by hash value instead of
    ///     canonical name order.
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
    ///     This method finds the NSEC3 record whose own hash matches a name's hash exactly.
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
    ///     This method computes a name's RFC 5155 §5 hash under a zone's own configured
    ///     algorithm, iteration count, and salt.
    /// </summary>
    private static byte[] HashFor(ZoneEntity zone, DnsName name) =>
        Nsec3Hasher.ComputeHash(
            1,
            name,
            zone.Nsec3Iterations,
            string.IsNullOrEmpty(zone.Nsec3SaltHex) ? [] : Convert.FromHexString(zone.Nsec3SaltHex)
        );

    /// <summary>
    ///     This method finds both a name's closest encloser and the "next closer name" --
    ///     RFC 5155 §7.2.1's term for the one name, one label longer than the closest encloser,
    ///     lying on the path down to <paramref name="name" />, which itself does not exist (that
    ///     is exactly what makes the encloser above it the *closest* one). Membership is tested
    ///     mode-aware -- via <see cref="FindExactMatch" /> -- since testing a plain candidate name
    ///     against NSEC3's own *hashed* owner names directly would never match.
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
    ///     This method builds the synthetic wildcard name that would answer under a given
    ///     encloser -- <c>*.encloser</c>.
    /// </summary>
    private static DnsName WildcardNameFor(DnsName encloser) =>
        new([
            [(byte)'*'],
            .. encloser.Labels,
        ]);

    /// <summary>
    ///     This method attaches a denial-of-existence record's own covering RRSIG(s) to it, or
    ///     returns an empty list if the record itself was not found (an empty, unsigned zone,
    ///     most likely, which should never happen for a zone that reached this code path at all).
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
    ///     This method removes duplicate records (by owner name, type, and RDATA) from a proof --
    ///     the closest-encloser proof and the wildcard non-existence proof can legitimately
    ///     resolve to the very same record.
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
    ///     This method compares two byte sequences by unsigned value, most significant byte
    ///     first -- the same order <see cref="ZoneSigningService" /> sorts an NSEC3 chain by.
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
