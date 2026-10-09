using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Dnssec;
using Ninlil.Dns.Server.Dnssec.Algorithms;

// Define our namespace.
namespace Ninlil.Dns.Server.Recursive.Validation;

/// <summary>
///     This class does the cryptographic checks of chain-of-trust building (RFC 4035 §5): an
///     RRset's RRSIG against trusted DNSKEYs (with the RRSIG and key checks of RFC 4035 §5.3.1 and a
///     work budget), a DNSKEY against a DS, and a DNSKEY against a trust anchor. <see cref="DnssecValidator" /> and <see cref="TrustAnchorRolloverService" /> use it.
/// </summary>
public static class RrsetSignatureVerifier
{
    /// <summary>
    ///     This method verifies an RRset the way <see cref="Verify" /> does, for callers that have
    ///     no zone of their own to name: the zone is the owner of the first DNSKEY in
    ///     <paramref name="candidateKeys" />, and the work budget is fresh. The rollover service
    ///     and tests use it; <see cref="DnssecValidator" /> calls <see cref="Verify" />.
    /// </summary>
    /// <param name="rrset">The RRset to verify, all of one owner name and type.</param>
    /// <param name="rrsigRecords">The DNSSEC records that came with <paramref name="rrset" />.</param>
    /// <param name="candidateKeys">The DNSKEY records trusted at this point in the chain.</param>
    /// <param name="now">The point in time to check each RRSIG's validity window against.</param>
    /// <returns>A <see langword="bool" /> indicating whether verification succeeded.</returns>
    public static bool TryVerify(
        IReadOnlyList<DnsResourceRecord> rrset,
        IReadOnlyList<DnsResourceRecord> rrsigRecords,
        IReadOnlyList<DnsResourceRecord> candidateKeys,
        DateTimeOffset now
    )
    {
        DnsResourceRecord firstKey = candidateKeys?.FirstOrDefault(record =>
            record.Type == DnsResourceRecordTypeEnum.DnsKey
        );

        if (firstKey is null)
            return false;

        return Verify(
            rrset,
            rrsigRecords,
            candidateKeys,
            firstKey.Name,
            now,
            new SignatureCheckBudget()
        ).IsVerified;
    }

    /// <summary>
    ///     This method checks whether at least one RRSIG over <paramref name="rrset" /> verifies
    ///     against one of <paramref name="trustedKeys" />, with the checks of RFC 4035 §5.3.1.
    ///     <para>
    ///         An RRSIG is considered only when its owner, class and Type Covered equal the
    ///         RRset's, its Signer's Name equals <paramref name="zoneApex" /> and is the owner or an
    ///         ancestor of it, its Labels field does not exceed the owner's label count, and
    ///         <paramref name="now" /> lies in its validity window. A key is considered only when it
    ///         is a DNSKEY owned by the signer name, has Protocol 3 (RFC 4034 §2.1.2), has the Zone
    ///         Key bit set (RFC 4034 §2.1.1), does not have the REVOKE bit set (RFC 5011 §2.1) unless
    ///         <paramref name="acceptRevokedKeys" /> is set, and matches the RRSIG's algorithm and
    ///         key tag. Algorithms the registry knows are verified even when this server no longer
    ///         signs with them.
    ///     </para>
    /// </summary>
    /// <param name="rrset">The RRset to verify, all of one owner name, class and type.</param>
    /// <param name="dnssecRecords">
    ///     The DNSSEC records that came with <paramref name="rrset" />; only RRSIGs are used.
    /// </param>
    /// <param name="trustedKeys">
    ///     The DNSKEY records trusted at this point in the chain. Only these are used, never a
    ///     DNSKEY found in <paramref name="dnssecRecords" />.
    /// </param>
    /// <param name="zoneApex">The apex of the zone whose keys are trusted: the expected signer.</param>
    /// <param name="now">The point in time to check each RRSIG's validity window against.</param>
    /// <param name="budget">
    ///     The work budget; the call starts a new RRset on it and fails once a limit is hit.
    /// </param>
    /// <param name="acceptRevokedKeys">
    ///     <see langword="true" /> only to check a self-signed revocation (RFC 5011 §2.1): the
    ///     revoked key itself signs the DNSKEY RRset, so a key with the REVOKE bit set may verify.
    ///     It is <see langword="false" /> everywhere else, where a revoked key is never trusted.
    /// </param>
    /// <returns>The outcome, with the verifying RRSIG or the reason for the failure.</returns>
    public static RrsetVerificationResult Verify(
        IReadOnlyList<DnsResourceRecord> rrset,
        IReadOnlyList<DnsResourceRecord> dnssecRecords,
        IReadOnlyList<DnsResourceRecord> trustedKeys,
        DnsName zoneApex,
        DateTimeOffset now,
        SignatureCheckBudget budget,
        bool acceptRevokedKeys = false
    )
    {
        if (rrset.Count == 0 || trustedKeys is null || zoneApex is null)
            return RrsetVerificationResult.Failure("There is no RRset or no trusted key to check.");

        DnsName ownerName = rrset[0].Name;
        DnsClassEnum recordClass = rrset[0].Class;
        DnsResourceRecordTypeEnum coveredType = rrset[0].Type;
        string reason = "No RRSIG covers the RRset.";

        budget.BeginRrset();

        // An RRset can have several RRSIGs (during an algorithm rollover, for example), so every
        // eligible RRSIG is tried against every key, and one match is enough. RRSIGs that fail a
        // check that needs no cryptography are skipped first, and the budget bounds the rest.
        foreach (DnsResourceRecord rrsigRecord in dnssecRecords)
        {
            if (
                rrsigRecord.Type != DnsResourceRecordTypeEnum.RrSig
                || rrsigRecord.Name != ownerName
                || rrsigRecord.Class != recordClass
            )
                continue;

            if (
                DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.RrSig, rrsigRecord.Data)
                    is not RrSigRecordData rrsig
                || rrsig.TypeCovered != coveredType
            )
                continue;

            // RFC 4035 §5.3.1: the signer must be the zone the keys belong to, and that zone must
            // contain the owner, or an attacker could present a signature from an unrelated zone.
            if (rrsig.SignerName != zoneApex || !IsAtOrBelow(ownerName, rrsig.SignerName))
            {
                reason = "An RRSIG's signer name is not the zone, or not an ancestor of the owner.";
                continue;
            }

            DateTimeOffset inception = DateTimeOffset.FromUnixTimeSeconds(rrsig.SignatureInception);
            DateTimeOffset expiration = DateTimeOffset.FromUnixTimeSeconds(
                rrsig.SignatureExpiration
            );

            if (now < inception || now > expiration)
            {
                reason = "An RRSIG is outside its validity period.";
                continue;
            }

            // A Labels value above the owner's label count is invalid (RFC 4035 §5.3.1) and has no
            // signed owner to rebuild.
            if (DnssecCanonicalization.ReconstructSignedOwner(ownerName, rrsig.Labels) is null)
            {
                reason = "An RRSIG's Labels field exceeds the owner name's label count.";
                continue;
            }

            if (
                !DnssecAlgorithmRegistry.TryGetAlgorithm(
                    rrsig.Algorithm,
                    out IDnssecSigningAlgorithm algorithm
                )
            )
            {
                reason = "An RRSIG uses an unsupported algorithm.";
                continue;
            }

            // Only a key with this RRSIG's algorithm and key tag is worth verifying against.
            foreach (DnsResourceRecord keyRecord in trustedKeys)
            {
                if (
                    keyRecord.Type != DnsResourceRecordTypeEnum.DnsKey
                    || keyRecord.Name != rrsig.SignerName
                    || keyRecord.Data.Length < 4
                    || keyRecord.Data[2] != DnsKeyRecordData.Protocol
                )
                    continue;

                if (
                    DnsRecordDataCodecRegistry.Decode(
                        DnsResourceRecordTypeEnum.DnsKey,
                        keyRecord.Data
                    )
                        is not DnsKeyRecordData dnsKey
                    || !dnsKey.IsZoneKey
                    || (dnsKey.IsRevoked && !acceptRevokedKeys)
                    || dnsKey.Algorithm != rrsig.Algorithm
                )
                    continue;

                if (DnssecKeyTag.Compute(dnsKey.Algorithm, keyRecord.Data) != rrsig.KeyTag)
                    continue;

                if (!budget.TryConsume())
                    return RrsetVerificationResult.Failure("Signature work limit reached.");

                byte[] signingInput = DnssecCanonicalization.BuildSigningInput(
                    rrsig,
                    ownerName,
                    recordClass,
                    rrset
                );

                if (algorithm.Verify(signingInput, rrsig.Signature, dnsKey.PublicKey))
                    return RrsetVerificationResult.Success(rrsig);

                reason = "No RRSIG verified against a trusted key.";
            }
        }

        return RrsetVerificationResult.Failure(reason);
    }

    /// <summary>
    ///     This method tells whether a DS RRset holds at least one record this validator can use:
    ///     a digest type <see cref="DsDigestCodec.IsSupported" /> and an algorithm in
    ///     <see cref="DnssecAlgorithmRegistry" />. When none is usable the zone is treated as
    ///     Insecure (RFC 4035 §5.2).
    /// </summary>
    /// <param name="dsRecords">The DS RRset published for a zone in its parent.</param>
    /// <returns><see langword="true" /> if a usable DS record exists.</returns>
    public static bool HasSupportedDs(IReadOnlyList<DnsResourceRecord> dsRecords) =>
        DecodeSupportedDs(dsRecords).Count > 0;

    /// <summary>
    ///     This method finds the records in a DNSKEY RRset that match the parent's DS RRset for
    ///     the same name, with key tag, algorithm and digest agreeing (RFC 4035 §5.2). DS records
    ///     with an unsupported digest type or algorithm are ignored. A SHA-1 DS is also ignored
    ///     when a SHA-256 or SHA-384 DS exists for the same key tag and algorithm (RFC 4509 §3).
    /// </summary>
    /// <param name="dnsKeyRecords">The child zone's DNSKEY RRset.</param>
    /// <param name="ownerName">The DNSKEY RRset's owner name, the child zone's apex.</param>
    /// <param name="dsRecords">The DS RRset published for the same name in the parent zone.</param>
    /// <returns>The matching DNSKEY records; empty if none matches.</returns>
    public static IReadOnlyList<DnsResourceRecord> FindKeysMatchingDs(
        IReadOnlyList<DnsResourceRecord> dnsKeyRecords,
        DnsName ownerName,
        IReadOnlyList<DnsResourceRecord> dsRecords
    )
    {
        List<DsRecordData> dsList = DecodeSupportedDs(dsRecords);

        // RFC 4509 §3: a validator that has the stronger digest must not accept the key on the
        // SHA-1 digest alone.
        dsList.RemoveAll(ds =>
            ds.DigestType == 1
            && dsList.Any(other =>
                other.DigestType is 2 or 4
                && other.KeyTag == ds.KeyTag
                && other.Algorithm == ds.Algorithm
            )
        );

        List<DnsResourceRecord> matches = [];

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
                {
                    matches.Add(keyRecord);
                    break;
                }
            }
        }

        return matches;
    }

    /// <summary>
    ///     This method decodes the DS records of an RRset and keeps those with a supported digest
    ///     type and a known algorithm.
    /// </summary>
    private static List<DsRecordData> DecodeSupportedDs(
        IReadOnlyList<DnsResourceRecord> dsRecords
    ) =>
        [
            .. dsRecords
                .Where(record => record.Type == DnsResourceRecordTypeEnum.DS)
                .Select(record =>
                    DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.DS, record.Data)
                )
                .OfType<DsRecordData>()
                .Where(ds =>
                    DsDigestCodec.IsSupported(ds.DigestType)
                    && DnssecAlgorithmRegistry.TryGetAlgorithm(ds.Algorithm, out _)
                ),
        ];

    /// <summary>
    ///     This method tells whether a name equals an ancestor or is below it.
    /// </summary>
    private static bool IsAtOrBelow(DnsName name, DnsName ancestor)
    {
        int skip = name.Labels.Count - ancestor.Labels.Count;

        return skip >= 0 && new DnsName([.. name.Labels.Skip(skip)]) == ancestor;
    }

    /// <summary>
    ///     This method finds the records in a DNSKEY RRset that match a trust anchor: the base of
    ///     the chain of trust, where there is no parent DS. Only a zone key (RFC 4034 §2.1.1)
    ///     without the REVOKE bit (RFC 5011 §2.1) can match, and an anchor whose digest type this
    ///     validator cannot compute is ignored.
    /// </summary>
    /// <param name="dnsKeyRecords">The DNSKEY RRset, normally the root's.</param>
    /// <param name="ownerName">The DNSKEY RRset's owner name.</param>
    /// <param name="anchors">The trust anchors to match.</param>
    /// <returns>The matching DNSKEY records; empty if none matches.</returns>
    public static IReadOnlyList<DnsResourceRecord> FindKeysMatchingTrustAnchor(
        IReadOnlyList<DnsResourceRecord> dnsKeyRecords,
        DnsName ownerName,
        IReadOnlyList<TrustAnchor> anchors
    )
    {
        List<DnsResourceRecord> matches = [];

        foreach (DnsResourceRecord keyRecord in dnsKeyRecords)
        {
            if (keyRecord.Type != DnsResourceRecordTypeEnum.DnsKey)
                continue;

            if (
                DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.DnsKey, keyRecord.Data)
                    is not DnsKeyRecordData dnsKey
                || !dnsKey.IsZoneKey
                || dnsKey.IsRevoked
            )
                continue;

            ushort keyTag = DnssecKeyTag.Compute(dnsKey.Algorithm, keyRecord.Data);

            foreach (TrustAnchor anchor in anchors)
            {
                if (
                    anchor.Algorithm != dnsKey.Algorithm
                    || anchor.KeyTag != keyTag
                    || !DsDigestCodec.IsSupported(anchor.DigestType)
                )
                    continue;

                byte[] expectedDigest = DsDigestCodec.Compute(
                    anchor.DigestType,
                    ownerName,
                    keyRecord.Data
                );

                if (expectedDigest.AsSpan().SequenceEqual(anchor.Digest))
                {
                    matches.Add(keyRecord);
                    break;
                }
            }
        }

        return matches;
    }
}
