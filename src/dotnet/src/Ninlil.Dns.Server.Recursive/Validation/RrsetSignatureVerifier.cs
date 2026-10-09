using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Dnssec;
using Ninlil.Dns.Server.Dnssec.Algorithms;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Validation;

/// <summary>
///     This class does the cryptographic checks of chain-of-trust building (RFC 4035 §5): an
///     RRset's RRSIG against trusted DNSKEYs, a DNSKEY against a DS, and a DNSKEY against a trust
///     anchor. <see cref="DnssecValidator" /> and <see cref="TrustAnchorRolloverService" /> use it.
/// </summary>
public static class RrsetSignatureVerifier
{
    /// <summary>
    ///     This method checks whether at least one RRSIG with <paramref name="rrset" />'s owner name
    ///     and covered type, inside its validity window, verifies against one of
    ///     <paramref name="candidateKeys" />. The RRSIG's signer name and labels count are not
    ///     checked.
    /// </summary>
    /// <param name="rrset">The RRset to verify, all of one owner name and type.</param>
    /// <param name="rrsigRecords">
    ///     The DNSSEC records that came with <paramref name="rrset" />; only RRSIGs with its owner
    ///     name and covered type are used.
    /// </param>
    /// <param name="candidateKeys">
    ///     The DNSKEY records trusted at this point in the chain. Only these are used, never a
    ///     DNSKEY found in <paramref name="rrsigRecords" />.
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

        // An RRset can have several RRSIGs (during an algorithm rollover, for example), so every
        // eligible RRSIG is tried against every key, and one match is enough. RRSIGs with the
        // wrong owner or type, outside their window or with an unsupported algorithm are skipped
        // before any cryptography.
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

            // A Labels value above the owner's label count is invalid (RFC 4035 §5.3.1) and has no
            // signed owner to rebuild. Other Labels checks belong to the signer-name checks.
            if (DnssecCanonicalization.ReconstructSignedOwner(ownerName, rrsig.Labels) is null)
                continue;

            if (
                !DnssecAlgorithmRegistry.TryGetAlgorithm(
                    rrsig.Algorithm,
                    out IDnssecSigningAlgorithm algorithm
                )
            )
                continue;

            // Only a key with this RRSIG's algorithm and key tag is worth verifying against.
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
    ///     This method finds the record in a DNSKEY RRset that matches the parent's DS RRset for
    ///     the same name, with key tag, algorithm and digest agreeing (RFC 4035 §5.2).
    /// </summary>
    /// <param name="dnsKeyRecords">The child zone's DNSKEY RRset.</param>
    /// <param name="ownerName">The DNSKEY RRset's owner name, the child zone's apex.</param>
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
    ///     This method finds the record in a DNSKEY RRset that matches a trust anchor: the base of
    ///     the chain of trust, where there is no parent DS.
    /// </summary>
    /// <param name="dnsKeyRecords">The DNSKEY RRset, normally the root's.</param>
    /// <param name="ownerName">The DNSKEY RRset's owner name.</param>
    /// <param name="anchors">The trust anchors to match.</param>
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
