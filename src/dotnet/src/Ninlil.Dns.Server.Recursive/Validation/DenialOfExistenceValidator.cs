using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Dnssec;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Validation;

/// <summary>
///     This class checks authenticated denial of existence (RFC 4035 §5.4, RFC 5155 §8): that
///     NSEC or NSEC3 records prove a name does not exist, that it exists without the queried type,
///     or that a wildcard answer had no closer match. <see cref="DnssecValidator" /> uses it for
///     negative answers and for proving a delegation has no DS.
///     <para>
///         Every method takes the apex of the zone that holds the records. Records owned outside
///         that zone are ignored, and so is any NSEC or NSEC3 at a delegation (NS set, SOA clear)
///         or at a DNAME when it is used as proof about names below its owner (RFC 6840 §4.1,
///         RFC 5155 §8.3).
///     </para>
///     <para>
///         The class checks only the structure (canonical order, hash ranges and type bitmaps).
///         The caller must verify with <see cref="RrsetSignatureVerifier" /> that every NSEC and
///         NSEC3 record is signed by a trusted key, or a forged record could prove anything.
///     </para>
/// </summary>
public static class DenialOfExistenceValidator
{
    /// <summary>
    ///     This constant is the highest NSEC3 iteration count the validator hashes with. A zone
    ///     above it gets <see cref="DenialOfExistenceOutcomeEnum.ProvenInsecure" /> without any
    ///     hashing, which bounds the CPU a hostile zone can cost (RFC 9276 §3.2).
    /// </summary>
    public const ushort MaxNsec3Iterations = 150;

    /// <summary>
    ///     This delegate computes an NSEC3 hash. It has the shape of
    ///     <see cref="Nsec3Hasher.ComputeHash" />, which is the default; a test passes a counting
    ///     version to observe how many hashes a check computes.
    /// </summary>
    public delegate byte[] Nsec3HashFunction(
        byte hashAlgorithm,
        DnsName name,
        ushort iterations,
        byte[] salt
    );

    /// <summary>
    ///     This method checks an NSEC NODATA proof for a name in the zone. It accepts, in order:
    ///     <list type="number">
    ///         <item>
    ///             an NSEC owned by the name whose bitmap lacks the type and CNAME. For a DS query
    ///             the bitmap must also have NS and lack SOA (RFC 6840 §4.4), or a spoofed
    ///             unsigned delegation could claim a signed record is unsigned. For any other type
    ///             an NSEC with NS and no SOA is the parent side of a delegation and is refused;
    ///         </item>
    ///         <item>
    ///             an empty non-terminal: an NSEC covering the name whose next name is below it;
    ///         </item>
    ///         <item>
    ///             a wildcard NODATA proof (RFC 4035 §3.1.3.4): an NSEC covering the name, which
    ///             gives the closest encloser, and an NSEC owned by the wildcard at the closest
    ///             encloser whose bitmap lacks the type and CNAME.
    ///         </item>
    ///     </list>
    /// </summary>
    /// <param name="queriedName">The name that exists without the queried type.</param>
    /// <param name="queriedType">The queried type.</param>
    /// <param name="zoneApex">The apex of the zone that holds the records.</param>
    /// <param name="dnssecRecords">The NSEC records of the response.</param>
    /// <returns>
    ///     <see cref="DenialOfExistenceOutcomeEnum.Proven" /> or
    ///     <see cref="DenialOfExistenceOutcomeEnum.NotProven" />.
    /// </returns>
    public static DenialOfExistenceOutcomeEnum VerifyNoData(
        DnsName queriedName,
        DnsResourceRecordTypeEnum queriedType,
        DnsName zoneApex,
        IReadOnlyList<DnsResourceRecord> dnssecRecords
    )
    {
        if (!IsAtOrBelow(queriedName, zoneApex))
            return DenialOfExistenceOutcomeEnum.NotProven;

        List<(DnsName Owner, NsecRecordData Data)> nsecs = DecodeNsecs(dnssecRecords, zoneApex);

        foreach ((DnsName owner, NsecRecordData data) in nsecs)
        {
            if (owner != queriedName)
                continue;

            // The name exists, so no other proof can apply; this record decides.
            if (
                data.Types.Contains(queriedType)
                || data.Types.Contains(DnsResourceRecordTypeEnum.Cname)
            )
                return DenialOfExistenceOutcomeEnum.NotProven;

            bool isParentSide = IsDelegationPoint(data);

            if (queriedType == DnsResourceRecordTypeEnum.DS)
                return isParentSide
                    ? DenialOfExistenceOutcomeEnum.Proven
                    : DenialOfExistenceOutcomeEnum.NotProven;

            return isParentSide
                ? DenialOfExistenceOutcomeEnum.NotProven
                : DenialOfExistenceOutcomeEnum.Proven;
        }

        (DnsName Owner, NsecRecordData Data)? covering = FindCoveringNsec(queriedName, nsecs);

        if (covering is null)
            return DenialOfExistenceOutcomeEnum.NotProven;

        if (IsProperAncestor(queriedName, covering.Value.Data.NextDomainName))
            return DenialOfExistenceOutcomeEnum.Proven;

        DnsName closestEncloser = DeriveClosestEncloser(queriedName, covering.Value);

        if (!IsUsableClosestEncloser(closestEncloser, queriedName, zoneApex))
            return DenialOfExistenceOutcomeEnum.NotProven;

        DnsName wildcard = closestEncloser.PrependLabels("*");

        return nsecs.Any(entry =>
            entry.Owner == wildcard
            && !entry.Data.Types.Contains(queriedType)
            && !entry.Data.Types.Contains(DnsResourceRecordTypeEnum.Cname)
        )
            ? DenialOfExistenceOutcomeEnum.Proven
            : DenialOfExistenceOutcomeEnum.NotProven;
    }

    /// <summary>
    ///     This method checks an NSEC NXDOMAIN proof (RFC 4035 §3.1.3.2): an NSEC covering the
    ///     name, and an NSEC covering the wildcard at the closest encloser. The closest encloser
    ///     comes from the covering NSEC itself: it is the longer of the name's common suffixes with
    ///     the NSEC's owner and with its next name. It need not own an NSEC, so an empty
    ///     non-terminal can be the closest encloser.
    /// </summary>
    /// <param name="queriedName">The name that does not exist.</param>
    /// <param name="zoneApex">The apex of the zone that holds the records.</param>
    /// <param name="dnssecRecords">The NSEC records of the response.</param>
    /// <returns>
    ///     <see cref="DenialOfExistenceOutcomeEnum.Proven" /> or
    ///     <see cref="DenialOfExistenceOutcomeEnum.NotProven" />.
    /// </returns>
    public static DenialOfExistenceOutcomeEnum VerifyNxDomain(
        DnsName queriedName,
        DnsName zoneApex,
        IReadOnlyList<DnsResourceRecord> dnssecRecords
    )
    {
        if (!IsAtOrBelow(queriedName, zoneApex))
            return DenialOfExistenceOutcomeEnum.NotProven;

        List<(DnsName Owner, NsecRecordData Data)> nsecs = DecodeNsecs(dnssecRecords, zoneApex);
        (DnsName Owner, NsecRecordData Data)? covering = FindCoveringNsec(queriedName, nsecs);

        if (covering is null)
            return DenialOfExistenceOutcomeEnum.NotProven;

        DnsName closestEncloser = DeriveClosestEncloser(queriedName, covering.Value);

        if (!IsUsableClosestEncloser(closestEncloser, queriedName, zoneApex))
            return DenialOfExistenceOutcomeEnum.NotProven;

        return FindCoveringNsec(closestEncloser.PrependLabels("*"), nsecs) is not null
            ? DenialOfExistenceOutcomeEnum.Proven
            : DenialOfExistenceOutcomeEnum.NotProven;
    }

    /// <summary>
    ///     This method checks the NSEC proof that goes with a wildcard answer (RFC 4035 §5.3.4):
    ///     the next closer name (the queried name cut to one label more than the RRSIG's Labels
    ///     field) must be covered, so no closer match exists.
    /// </summary>
    /// <param name="queriedName">The originally queried name, before wildcard expansion.</param>
    /// <param name="signedOwnerLabels">The Labels field of the RRSIG over the answer.</param>
    /// <param name="zoneApex">The apex of the zone that holds the records.</param>
    /// <param name="dnssecRecords">The NSEC records of the response.</param>
    /// <returns>
    ///     <see cref="DenialOfExistenceOutcomeEnum.Proven" /> or
    ///     <see cref="DenialOfExistenceOutcomeEnum.NotProven" />.
    /// </returns>
    public static DenialOfExistenceOutcomeEnum VerifyWildcardAnswer(
        DnsName queriedName,
        byte signedOwnerLabels,
        DnsName zoneApex,
        IReadOnlyList<DnsResourceRecord> dnssecRecords
    )
    {
        if (
            !IsAtOrBelow(queriedName, zoneApex)
            || signedOwnerLabels < zoneApex.Labels.Count
            || signedOwnerLabels >= queriedName.Labels.Count
        )
            return DenialOfExistenceOutcomeEnum.NotProven;

        DnsName nextCloser = Suffix(queriedName, signedOwnerLabels + 1);

        return FindCoveringNsec(nextCloser, DecodeNsecs(dnssecRecords, zoneApex)) is not null
            ? DenialOfExistenceOutcomeEnum.Proven
            : DenialOfExistenceOutcomeEnum.NotProven;
    }

    /// <summary>
    ///     This method checks an NSEC3 NODATA proof for a name in the zone (RFC 5155 §8.5 to
    ///     §8.7):
    ///     <list type="bullet">
    ///         <item>
    ///             an NSEC3 matching the name whose bitmap lacks the type and CNAME. For DS it must
    ///             also have NS and lack SOA; for any other type an NSEC3 with NS and no SOA is the
    ///             parent side of a delegation and is refused;
    ///         </item>
    ///         <item>
    ///             for DS with no matching NSEC3, a closest provable encloser proof whose next
    ///             closer name is covered by an Opt-Out NSEC3 (the answer is
    ///             <see cref="DenialOfExistenceOutcomeEnum.ProvenInsecure" />);
    ///         </item>
    ///         <item>
    ///             for other types with no matching NSEC3, a wildcard NODATA proof: the closest
    ///             encloser proof plus an NSEC3 matching the wildcard at the closest encloser whose
    ///             bitmap lacks the type and CNAME.
    ///         </item>
    ///     </list>
    ///     A zone above <see cref="MaxNsec3Iterations" /> gives
    ///     <see cref="DenialOfExistenceOutcomeEnum.ProvenInsecure" /> without hashing.
    /// </summary>
    /// <param name="queriedName">The name that exists without the queried type.</param>
    /// <param name="queriedType">The queried type.</param>
    /// <param name="zoneApex">The apex of the zone that holds the records.</param>
    /// <param name="dnssecRecords">The NSEC3 records of the response.</param>
    /// <param name="hashFunction">The hash function, or <see langword="null" /> for SHA-1.</param>
    /// <returns>The outcome of the check.</returns>
    public static DenialOfExistenceOutcomeEnum VerifyNoDataNsec3(
        DnsName queriedName,
        DnsResourceRecordTypeEnum queriedType,
        DnsName zoneApex,
        IReadOnlyList<DnsResourceRecord> dnssecRecords,
        Nsec3HashFunction hashFunction = null
    )
    {
        if (
            !TryPrepareNsec3(
                queriedName,
                zoneApex,
                dnssecRecords,
                hashFunction,
                out Nsec3Set set,
                out DenialOfExistenceOutcomeEnum early
            )
        )
            return early;

        Nsec3Entry match = set.FindMatch(queriedName);

        if (match is not null)
        {
            IReadOnlyList<DnsResourceRecordTypeEnum> types = match.Data.Types;

            if (types.Contains(queriedType) || types.Contains(DnsResourceRecordTypeEnum.Cname))
                return DenialOfExistenceOutcomeEnum.NotProven;

            bool isParentSide = IsDelegationPoint(types);

            // DS lives on the parent side of a cut, so a DS proof needs the cut to be there.
            return isParentSide == (queriedType == DnsResourceRecordTypeEnum.DS)
                ? DenialOfExistenceOutcomeEnum.Proven
                : DenialOfExistenceOutcomeEnum.NotProven;
        }

        (DnsName closestEncloser, Nsec3Entry encloserEntry) = set.FindClosestEncloser(
            queriedName,
            zoneApex
        );

        if (closestEncloser is null || IsDelegationPointOrDname(encloserEntry.Data.Types))
            return DenialOfExistenceOutcomeEnum.NotProven;

        Nsec3Entry nextCloserCover = set.FindCover(
            Suffix(queriedName, closestEncloser.Labels.Count + 1)
        );

        if (nextCloserCover is null)
            return DenialOfExistenceOutcomeEnum.NotProven;

        if (queriedType == DnsResourceRecordTypeEnum.DS)
            return nextCloserCover.Data.IsOptOut
                ? DenialOfExistenceOutcomeEnum.ProvenInsecure
                : DenialOfExistenceOutcomeEnum.NotProven;

        Nsec3Entry wildcardMatch = set.FindMatch(closestEncloser.PrependLabels("*"));

        return
            wildcardMatch is not null
            && !wildcardMatch.Data.Types.Contains(queriedType)
            && !wildcardMatch.Data.Types.Contains(DnsResourceRecordTypeEnum.Cname)
            ? DenialOfExistenceOutcomeEnum.Proven
            : DenialOfExistenceOutcomeEnum.NotProven;
    }

    /// <summary>
    ///     This method checks an NSEC3 NXDOMAIN proof (RFC 5155 §8.4) built on the closest
    ///     encloser proof of §8.3: an NSEC3 matching the closest encloser (which must not be a
    ///     delegation or a DNAME), one covering the next closer name and one covering the wildcard
    ///     at the closest encloser. If the NSEC3 covering the next closer name has the Opt-Out
    ///     flag, the name may be an unsigned delegation and the result is
    ///     <see cref="DenialOfExistenceOutcomeEnum.ProvenInsecure" />. A zone above
    ///     <see cref="MaxNsec3Iterations" /> gives the same result without hashing.
    /// </summary>
    /// <param name="queriedName">The name that does not exist.</param>
    /// <param name="zoneApex">The apex of the zone that holds the records.</param>
    /// <param name="dnssecRecords">The NSEC3 records of the response.</param>
    /// <param name="hashFunction">The hash function, or <see langword="null" /> for SHA-1.</param>
    /// <returns>The outcome of the check.</returns>
    public static DenialOfExistenceOutcomeEnum VerifyNxDomainNsec3(
        DnsName queriedName,
        DnsName zoneApex,
        IReadOnlyList<DnsResourceRecord> dnssecRecords,
        Nsec3HashFunction hashFunction = null
    )
    {
        if (
            !TryPrepareNsec3(
                queriedName,
                zoneApex,
                dnssecRecords,
                hashFunction,
                out Nsec3Set set,
                out DenialOfExistenceOutcomeEnum early
            )
        )
            return early;

        // A matching NSEC3 means the name exists.
        if (set.FindMatch(queriedName) is not null)
            return DenialOfExistenceOutcomeEnum.NotProven;

        (DnsName closestEncloser, Nsec3Entry encloserEntry) = set.FindClosestEncloser(
            queriedName,
            zoneApex
        );

        if (closestEncloser is null || IsDelegationPointOrDname(encloserEntry.Data.Types))
            return DenialOfExistenceOutcomeEnum.NotProven;

        Nsec3Entry nextCloserCover = set.FindCover(
            Suffix(queriedName, closestEncloser.Labels.Count + 1)
        );

        if (nextCloserCover is null)
            return DenialOfExistenceOutcomeEnum.NotProven;

        if (set.FindCover(closestEncloser.PrependLabels("*")) is null)
            return DenialOfExistenceOutcomeEnum.NotProven;

        return nextCloserCover.Data.IsOptOut
            ? DenialOfExistenceOutcomeEnum.ProvenInsecure
            : DenialOfExistenceOutcomeEnum.Proven;
    }

    /// <summary>
    ///     This method checks the NSEC3 proof that goes with a wildcard answer (RFC 5155 §8.8):
    ///     an NSEC3 covers the next closer name (the queried name cut to one label more than the
    ///     RRSIG's Labels field). A zone above <see cref="MaxNsec3Iterations" /> gives
    ///     <see cref="DenialOfExistenceOutcomeEnum.ProvenInsecure" /> without hashing.
    /// </summary>
    /// <param name="queriedName">The originally queried name, before wildcard expansion.</param>
    /// <param name="signedOwnerLabels">The Labels field of the RRSIG over the answer.</param>
    /// <param name="zoneApex">The apex of the zone that holds the records.</param>
    /// <param name="dnssecRecords">The NSEC3 records of the response.</param>
    /// <param name="hashFunction">The hash function, or <see langword="null" /> for SHA-1.</param>
    /// <returns>The outcome of the check.</returns>
    public static DenialOfExistenceOutcomeEnum VerifyWildcardAnswerNsec3(
        DnsName queriedName,
        byte signedOwnerLabels,
        DnsName zoneApex,
        IReadOnlyList<DnsResourceRecord> dnssecRecords,
        Nsec3HashFunction hashFunction = null
    )
    {
        if (
            !TryPrepareNsec3(
                queriedName,
                zoneApex,
                dnssecRecords,
                hashFunction,
                out Nsec3Set set,
                out DenialOfExistenceOutcomeEnum early
            )
        )
            return early;

        if (
            signedOwnerLabels < zoneApex.Labels.Count
            || signedOwnerLabels >= queriedName.Labels.Count
        )
            return DenialOfExistenceOutcomeEnum.NotProven;

        return set.FindCover(Suffix(queriedName, signedOwnerLabels + 1)) is not null
            ? DenialOfExistenceOutcomeEnum.Proven
            : DenialOfExistenceOutcomeEnum.NotProven;
    }

    /// <summary>
    ///     This method filters and checks the NSEC3 records before any proof logic runs. It
    ///     returns <see langword="true" /> when <paramref name="set" /> is ready; otherwise
    ///     <paramref name="early" /> holds the final answer: not proven (name outside the zone, no
    ///     usable record, records that disagree on parameters) or proven insecure (iterations above
    ///     the cap, decided before any hash is computed).
    /// </summary>
    private static bool TryPrepareNsec3(
        DnsName queriedName,
        DnsName zoneApex,
        IReadOnlyList<DnsResourceRecord> records,
        Nsec3HashFunction hashFunction,
        out Nsec3Set set,
        out DenialOfExistenceOutcomeEnum early
    )
    {
        set = null;
        early = DenialOfExistenceOutcomeEnum.NotProven;

        if (!IsAtOrBelow(queriedName, zoneApex))
            return false;

        List<Nsec3Entry> entries = DecodeNsec3s(records, zoneApex);

        if (entries.Count == 0)
            return false;

        // RFC 5155 §8.2: all NSEC3 records of one proof must share their parameters.
        Nsec3RecordData first = entries[0].Data;

        if (
            entries.Any(entry =>
                entry.Data.HashAlgorithm != first.HashAlgorithm
                || entry.Data.Iterations != first.Iterations
                || !entry.Data.Salt.AsSpan().SequenceEqual(first.Salt)
            )
        )
            return false;

        if (first.Iterations > MaxNsec3Iterations)
        {
            early = DenialOfExistenceOutcomeEnum.ProvenInsecure;
            return false;
        }

        set = new Nsec3Set(entries, first, hashFunction ?? Nsec3Hasher.ComputeHash);
        return true;
    }

    /// <summary>
    ///     This method finds an NSEC record whose owner-to-next range covers a name, including the
    ///     last NSEC in the zone, whose range wraps around to the apex. An NSEC at a delegation or
    ///     DNAME above the name is skipped: it proves nothing about names below it (RFC 6840 §4.1).
    /// </summary>
    private static (DnsName Owner, NsecRecordData Data)? FindCoveringNsec(
        DnsName name,
        List<(DnsName Owner, NsecRecordData Data)> nsecs
    )
    {
        foreach ((DnsName owner, NsecRecordData data) in nsecs)
        {
            if (IsProperAncestor(owner, name) && IsDelegationPointOrDname(data.Types))
                continue;

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
    ///     This method derives the closest encloser of a name from the NSEC that covers it: the
    ///     longer of the name's common suffixes with the NSEC's owner and with its next name
    ///     (RFC 4035 §5.4). The name's closest encloser is its longest ancestor that is also an
    ///     ancestor of, or equal to, one end of the covering range.
    /// </summary>
    private static DnsName DeriveClosestEncloser(
        DnsName name,
        (DnsName Owner, NsecRecordData Data) covering
    )
    {
        DnsName withOwner = CommonSuffix(name, covering.Owner);
        DnsName withNext = CommonSuffix(name, covering.Data.NextDomainName);

        return withOwner.Labels.Count >= withNext.Labels.Count ? withOwner : withNext;
    }

    /// <summary>
    ///     This method checks that a derived closest encloser is a proper ancestor of the queried
    ///     name (equal means the name is an empty non-terminal, which exists) and lies in the zone.
    /// </summary>
    private static bool IsUsableClosestEncloser(
        DnsName closestEncloser,
        DnsName queriedName,
        DnsName zoneApex
    ) => IsProperAncestor(closestEncloser, queriedName) && IsAtOrBelow(closestEncloser, zoneApex);

    /// <summary>
    ///     This method tells whether an NSEC bitmap marks the parent side of a delegation: NS set
    ///     and SOA clear.
    /// </summary>
    private static bool IsDelegationPoint(NsecRecordData data) => IsDelegationPoint(data.Types);

    /// <summary>
    ///     This method tells whether a type bitmap marks the parent side of a delegation: NS set
    ///     and SOA clear.
    /// </summary>
    private static bool IsDelegationPoint(IReadOnlyList<DnsResourceRecordTypeEnum> types) =>
        types.Contains(DnsResourceRecordTypeEnum.NS)
        && !types.Contains(DnsResourceRecordTypeEnum.Soa);

    /// <summary>
    ///     This method tells whether a type bitmap marks a name that cannot be the closest
    ///     encloser or cover names below it: a delegation point or a DNAME owner.
    /// </summary>
    private static bool IsDelegationPointOrDname(IReadOnlyList<DnsResourceRecordTypeEnum> types) =>
        IsDelegationPoint(types) || types.Contains(DnsResourceRecordTypeEnum.Dname);

    /// <summary>
    ///     This method tells whether a name equals an ancestor or is below it.
    /// </summary>
    private static bool IsAtOrBelow(DnsName name, DnsName ancestor)
    {
        int offset = name.Labels.Count - ancestor.Labels.Count;

        return offset >= 0 && Suffix(name, ancestor.Labels.Count) == ancestor;
    }

    /// <summary>
    ///     This method tells whether a name is strictly above another.
    /// </summary>
    private static bool IsProperAncestor(DnsName ancestor, DnsName name) =>
        ancestor.Labels.Count < name.Labels.Count && IsAtOrBelow(name, ancestor);

    /// <summary>
    ///     This method returns the rightmost labels of a name, the ancestor with that many
    ///     labels.
    /// </summary>
    private static DnsName Suffix(DnsName name, int labelCount) =>
        new([.. name.Labels.Skip(name.Labels.Count - labelCount)]);

    /// <summary>
    ///     This method returns the longest name that both names are at or below.
    /// </summary>
    private static DnsName CommonSuffix(DnsName left, DnsName right)
    {
        int shared = 0;
        int limit = Math.Min(left.Labels.Count, right.Labels.Count);

        while (shared < limit && Suffix(left, shared + 1) == Suffix(right, shared + 1))
            shared++;

        return Suffix(left, shared);
    }

    /// <summary>
    ///     This class holds the NSEC3 records of one proof (already filtered to one zone and one
    ///     parameter set) and answers the match and cover lookups the proofs need. It memoizes the
    ///     hash of each name, because the closest encloser walk and the wildcard lookups hash the
    ///     same names repeatedly.
    /// </summary>
    private sealed class Nsec3Set(
        List<Nsec3Entry> entries,
        Nsec3RecordData parameters,
        Nsec3HashFunction hashFunction
    )
    {
        private readonly Dictionary<DnsName, byte[]> hashes = [];

        /// <summary>
        ///     This method returns the hash of a name under the proof's parameters.
        /// </summary>
        private byte[] HashOf(DnsName name)
        {
            if (!hashes.TryGetValue(name, out byte[] hash))
            {
                hash = hashFunction(
                    parameters.HashAlgorithm,
                    name,
                    parameters.Iterations,
                    parameters.Salt
                );
                hashes[name] = hash;
            }

            return hash;
        }

        /// <summary>
        ///     This method finds the NSEC3 record whose owner hash equals the name's hash.
        /// </summary>
        public Nsec3Entry FindMatch(DnsName name)
        {
            byte[] hash = HashOf(name);

            return entries.FirstOrDefault(entry => entry.Hash.AsSpan().SequenceEqual(hash));
        }

        /// <summary>
        ///     This method finds the NSEC3 record whose range strictly covers the name's hash.
        /// </summary>
        public Nsec3Entry FindCover(DnsName name)
        {
            byte[] hash = HashOf(name);

            return entries.FirstOrDefault(entry => CoversHash(entry, hash));
        }

        /// <summary>
        ///     This method finds the closest provable encloser (RFC 5155 §8.3): the longest proper
        ///     ancestor of the name, at or below the zone apex, that has a matching NSEC3 record.
        ///     It returns nulls when none matches.
        /// </summary>
        public (DnsName Name, Nsec3Entry Entry) FindClosestEncloser(DnsName name, DnsName zoneApex)
        {
            for (int count = name.Labels.Count - 1; count >= zoneApex.Labels.Count; count--)
            {
                DnsName candidate = Suffix(name, count);
                Nsec3Entry match = FindMatch(candidate);

                if (match is not null)
                    return (candidate, match);
            }

            return (null, null);
        }
    }

    /// <summary>
    ///     This record is an NSEC3 record decoded for matching: its owner name, the hash taken
    ///     from the owner's first label and its data.
    /// </summary>
    private sealed record Nsec3Entry(DnsName Owner, byte[] Hash, Nsec3RecordData Data);

    /// <summary>
    ///     This method checks whether an NSEC3 record's hash range, including the wraparound case,
    ///     covers a target hash.
    /// </summary>
    private static bool CoversHash(Nsec3Entry entry, byte[] targetHash)
    {
        byte[] ownerHash = entry.Hash;
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
    ///     This method decodes the NSEC records owned at or below the zone apex; records of other
    ///     zones prove nothing.
    /// </summary>
    private static List<(DnsName Owner, NsecRecordData Data)> DecodeNsecs(
        IReadOnlyList<DnsResourceRecord> records,
        DnsName zoneApex
    ) =>
        [
            .. records
                .Where(record =>
                    record.Type == DnsResourceRecordTypeEnum.Nsec
                    && IsAtOrBelow(record.Name, zoneApex)
                )
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
    ///     This method decodes the usable NSEC3 records: owned exactly one label below the zone
    ///     apex, with a hash algorithm this validator can compute (others are ignored, RFC 5155
    ///     §8.1) and an owner hash that is as long as the next hashed owner name.
    /// </summary>
    private static List<Nsec3Entry> DecodeNsec3s(
        IReadOnlyList<DnsResourceRecord> records,
        DnsName zoneApex
    )
    {
        List<Nsec3Entry> entries = [];

        foreach (DnsResourceRecord record in records)
        {
            if (
                record.Type != DnsResourceRecordTypeEnum.Nsec3
                || record.Name.Labels.Count != zoneApex.Labels.Count + 1
                || !IsAtOrBelow(record.Name, zoneApex)
                || DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.Nsec3, record.Data)
                    is not Nsec3RecordData data
                || !Nsec3Hasher.IsSupported(data.HashAlgorithm)
            )
                continue;

            // A label that is not base32hex is unusable, like a length mismatch.
            if (!Nsec3Hasher.TryExtractHashFromOwnerName(record.Name, out byte[] ownerHash))
                continue;

            if (ownerHash.Length == 0 || ownerHash.Length != data.NextHashedOwnerName.Length)
                continue;

            entries.Add(new Nsec3Entry(record.Name, ownerHash, data));
        }

        return entries;
    }
}
