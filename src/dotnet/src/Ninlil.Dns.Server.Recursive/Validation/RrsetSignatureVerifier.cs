using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Dnssec;
using Ninlil.Dns.Server.Dnssec.Algorithms;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Validation;

/// <summary>
///     This class verifies an RRset's own RRSIG against a set of candidate DNSKEY records --
///     the cryptographic check every step of RFC 4035 §5.3's chain-of-trust building comes down
///     to, whether the RRset being checked is an ordinary answer, a DS RRset, or a DNSKEY RRset
///     verifying itself against a trust anchor.
/// </summary>
public static class RrsetSignatureVerifier
{
    /// <summary>
    ///     This method checks whether at least one RRSIG -- sharing <paramref name="rrset" />'s
    ///     own owner name and covering its type -- verifies against at least one of
    ///     <paramref name="candidateKeys" />, and is currently within its own validity window.
    /// </summary>
    /// <param name="rrset">The RRset to verify -- expected to already share one owner name and type.</param>
    /// <param name="rrsigRecords">
    ///     Every RRSIG (and, commonly, NSEC/NSEC3/other DNSSEC record) that accompanied
    ///     <paramref name="rrset" /> -- only the ones that are actually RRSIGs, sharing its owner
    ///     name, and covering its type are considered.
    /// </param>
    /// <param name="candidateKeys">
    ///     Every DNSKEY record already trusted at this point in the chain -- only these, never
    ///     any DNSKEY that might incidentally also be present in <paramref name="rrsigRecords" />
    ///     itself, are eligible to verify against.
    /// </param>
    /// <param name="now">The point in time to check each RRSIG's validity window against.</param>
    /// <returns>A <see langword="bool" /> indicating whether verification succeeded.</returns>
    public static bool TryVerify(
        IReadOnlyList<DnsResourceRecord> rrset,
        IReadOnlyList<DnsResourceRecord> rrsigRecords,
        IReadOnlyList<DnsResourceRecord> candidateKeys,
        DateTimeOffset now
    )
    {
        if (rrset.Count == 0 || candidateKeys is null)
            return false;

        DnsName ownerName = rrset[0].Name;
        DnsResourceRecordTypeEnum coveredType = rrset[0].Type;

        // An RRset can legitimately carry more than one RRSIG (a zone mid-algorithm-rollover,
        // most commonly), and any one candidate key might correspond to any one of them -- so
        // this is a genuine cross product, not a single linear scan: every RRSIG that plausibly
        // covers this RRset is tried against every candidate key, and the whole method succeeds
        // the moment any single (RRSIG, key) pairing verifies. The outer loop first narrows down
        // to RRSIGs that are structurally eligible at all (right owner name, right covered type,
        // currently inside their own inception/expiration window, and a supported algorithm)
        // before ever touching a key, so an obviously-inapplicable RRSIG never even reaches the
        // expensive cryptographic verification step.
        foreach (DnsResourceRecord rrsigRecord in rrsigRecords)
        {
            if (
                rrsigRecord.Type != DnsResourceRecordTypeEnum.RrSig
                || rrsigRecord.Name != ownerName
            )
                continue;

            if (
                DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.RrSig, rrsigRecord.Data)
                    is not RrSigRecordData rrsig
                || rrsig.TypeCovered != coveredType
            )
                continue;

            DateTimeOffset inception = DateTimeOffset.FromUnixTimeSeconds(rrsig.SignatureInception);
            DateTimeOffset expiration = DateTimeOffset.FromUnixTimeSeconds(
                rrsig.SignatureExpiration
            );

            if (now < inception || now > expiration)
                continue;

            if (
                !DnssecAlgorithmRegistry.TryGetAlgorithm(
                    rrsig.Algorithm,
                    out IDnssecSigningAlgorithm algorithm
                )
            )
                continue;

            // Only a candidate key whose own algorithm and key tag match this specific RRSIG is
            // worth the cost of actually rebuilding the canonical signing input and running the
            // real cryptographic verification against it.
            foreach (DnsResourceRecord keyRecord in candidateKeys)
            {
                if (keyRecord.Type != DnsResourceRecordTypeEnum.DnsKey)
                    continue;

                if (
                    DnsRecordDataCodecRegistry.Decode(
                        DnsResourceRecordTypeEnum.DnsKey,
                        keyRecord.Data
                    )
                        is not DnsKeyRecordData dnsKey
                    || dnsKey.Algorithm != rrsig.Algorithm
                )
                    continue;

                if (DnssecKeyTag.Compute(dnsKey.Algorithm, keyRecord.Data) != rrsig.KeyTag)
                    continue;

                byte[] signingInput = DnssecCanonicalization.BuildSigningInput(
                    rrsig,
                    ownerName,
                    rrset[0].Class,
                    rrset
                );

                if (algorithm.Verify(signingInput, rrsig.Signature, dnsKey.PublicKey))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     This method finds whichever record in a DNSKEY RRset matches a DS RRset published for
    ///     the same name in the parent zone -- key tag, algorithm, and digest all agreeing -- per
    ///     RFC 4035 §5.2.
    /// </summary>
    /// <param name="dnsKeyRecords">The child zone's own DNSKEY RRset.</param>
    /// <param name="ownerName">The DNSKEY RRset's owner name -- the child zone's own apex.</param>
    /// <param name="dsRecords">The DS RRset published for the same name in the parent zone.</param>
    /// <returns>The matching DNSKEY record, or <see langword="null" /> if none matches.</returns>
    public static DnsResourceRecord FindKeyMatchingDs(
        IReadOnlyList<DnsResourceRecord> dnsKeyRecords,
        DnsName ownerName,
        IReadOnlyList<DnsResourceRecord> dsRecords
    )
    {
        List<DsRecordData> dsList =
        [
            .. dsRecords
                .Where(record => record.Type == DnsResourceRecordTypeEnum.DS)
                .Select(record =>
                    DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.DS, record.Data)
                )
                .OfType<DsRecordData>(),
        ];

        foreach (DnsResourceRecord keyRecord in dnsKeyRecords)
        {
            if (keyRecord.Type != DnsResourceRecordTypeEnum.DnsKey)
                continue;

            if (
                DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.DnsKey, keyRecord.Data)
                is not DnsKeyRecordData dnsKey
            )
                continue;

            ushort keyTag = DnssecKeyTag.Compute(dnsKey.Algorithm, keyRecord.Data);

            foreach (DsRecordData ds in dsList)
            {
                if (ds.Algorithm != dnsKey.Algorithm || ds.KeyTag != keyTag)
                    continue;

                byte[] expectedDigest = DsDigestCodec.Compute(
                    ds.DigestType,
                    ownerName,
                    keyRecord.Data
                );

                if (expectedDigest.AsSpan().SequenceEqual(ds.Digest))
                    return keyRecord;
            }
        }

        return null;
    }

    /// <summary>
    ///     This method finds whichever record in the root zone's own DNSKEY RRset matches a
    ///     hardcoded trust anchor -- the base case of the chain of trust, with no parent DS to
    ///     check against instead.
    /// </summary>
    /// <param name="dnsKeyRecords">The root zone's own DNSKEY RRset.</param>
    /// <param name="ownerName">The DNSKEY RRset's owner name -- the root.</param>
    /// <param name="anchors">This resolver's own configured trust anchors.</param>
    /// <returns>The matching DNSKEY record, or <see langword="null" /> if none matches.</returns>
    public static DnsResourceRecord FindKeyMatchingTrustAnchor(
        IReadOnlyList<DnsResourceRecord> dnsKeyRecords,
        DnsName ownerName,
        IReadOnlyList<TrustAnchor> anchors
    )
    {
        foreach (DnsResourceRecord keyRecord in dnsKeyRecords)
        {
            if (keyRecord.Type != DnsResourceRecordTypeEnum.DnsKey)
                continue;

            if (
                DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.DnsKey, keyRecord.Data)
                is not DnsKeyRecordData dnsKey
            )
                continue;

            ushort keyTag = DnssecKeyTag.Compute(dnsKey.Algorithm, keyRecord.Data);

            foreach (TrustAnchor anchor in anchors)
            {
                if (anchor.Algorithm != dnsKey.Algorithm || anchor.KeyTag != keyTag)
                    continue;

                byte[] expectedDigest = DsDigestCodec.Compute(
                    anchor.DigestType,
                    ownerName,
                    keyRecord.Data
                );

                if (expectedDigest.AsSpan().SequenceEqual(anchor.Digest))
                    return keyRecord;
            }
        }

        return null;
    }
}
