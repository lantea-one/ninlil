using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Dnssec;
using Ninlil.Dns.Server.Dnssec.Algorithms;
using Ninlil.Dns.Server.Recursive.Validation;

namespace Ninlil.Dns.Server.Recursive.Tests.Support;

/// <summary>
///     This class builds real, cryptographically valid DNSSEC material (DNSKEY/DS/RRSIG) for
///     tests that need an actual chain of trust to walk, rather than faking the verification
///     result itself -- Ed25519 throughout, since it needs no key-size parameter and is the
///     cheapest algorithm this repo's registry already supports.
/// </summary>
public sealed class DnssecTestFixture
{
    private readonly IDnssecSigningAlgorithm _algorithm;

    public DnssecTestFixture()
    {
        DnssecAlgorithmRegistry.TryGetAlgorithm(DnssecAlgorithmEnum.Ed25519, out _algorithm);
    }

    public sealed record ZoneKey(
        DnssecKeyPair KeyPair,
        DnsResourceRecord DnsKeyRecord,
        ushort KeyTag
    );

    /// <summary>
    ///     This method generates a fresh zone-signing key and its own <c>DNSKEY</c> record.
    /// </summary>
    /// <param name="owner">The key's own owner name.</param>
    /// <param name="ttl">The generated <c>DNSKEY</c> record's own TTL.</param>
    /// <param name="isSecureEntryPoint">
    ///     Whether the generated key's own SEP (secure entry point / KSK) bit is set --
    ///     <see langword="false" /> by default (the simplest DNSKEY flavor most DNSSEC
    ///     validation logic branches on), but RFC 5011 §7.2 requires this bit set on any key a
    ///     trust-anchor-rollover test wants treated as a real trust point candidate at all -- set
    ///     <see langword="true" /> for exactly that case.
    /// </param>
    public ZoneKey GenerateZoneKey(DnsName owner, uint ttl = 3600, bool isSecureEntryPoint = false)
    {
        DnssecKeyPair keyPair = _algorithm.GenerateKeyPair();
        DnsKeyRecordData dnsKeyData = new(
            true,
            isSecureEntryPoint,
            DnssecAlgorithmEnum.Ed25519,
            keyPair.PublicKeyWireFormat
        );
        byte[] rdata = DnsRecordDataCodecRegistry.Encode(
            DnsResourceRecordTypeEnum.DnsKey,
            dnsKeyData
        );
        DnsResourceRecord record = new(
            owner,
            DnsResourceRecordTypeEnum.DnsKey,
            DnsClassEnum.Internet,
            ttl,
            rdata
        );

        // The key tag is computed over the already-wire-encoded RDATA, not the key material
        // directly, matching the real algorithm every validator (and every signer) must use so a
        // key tag computed here matches what a real resolver would compute for the same key.
        ushort keyTag = DnssecKeyTag.Compute(DnssecAlgorithmEnum.Ed25519, rdata);

        return new ZoneKey(keyPair, record, keyTag);
    }

    /// <summary>
    ///     This method signs an RRset with a zone key, returning the resulting RRSIG record.
    /// </summary>
    public DnsResourceRecord Sign(
        ZoneKey key,
        DnsName signerName,
        IReadOnlyList<DnsResourceRecord> rrset,
        DateTimeOffset? inception = null,
        DateTimeOffset? expiration = null
    )
    {
        DnsResourceRecord first = rrset[0];
        // Default to a validity window that is already open (started a day ago) and will stay
        // open for a month, so an ordinary test signing a record never has to reason about clock
        // skew or a signature expiring mid-run -- a test that specifically needs an expired or
        // not-yet-valid signature passes its own inception/expiration explicitly instead.
        DateTimeOffset actualInception = inception ?? DateTimeOffset.UtcNow.AddDays(-1);
        DateTimeOffset actualExpiration = expiration ?? DateTimeOffset.UtcNow.AddDays(30);

        RrSigRecordData unsigned = new(
            first.Type,
            DnssecAlgorithmEnum.Ed25519,
            (byte)first.Name.Labels.Count,
            first.TimeToLive,
            (uint)actualExpiration.ToUnixTimeSeconds(),
            (uint)actualInception.ToUnixTimeSeconds(),
            key.KeyTag,
            signerName,
            []
        );

        // RFC 4034's own canonical form (owner names lowercased, RRset sorted, TTLs normalized to
        // the RRSIG's own original-TTL field) has to be rebuilt here exactly as a real validator
        // would, or a signature this fixture produces would verify only by accident.
        byte[] signingInput = DnssecCanonicalization.BuildSigningInput(
            unsigned,
            first.Name,
            first.Class,
            rrset
        );
        byte[] signature = _algorithm.Sign(signingInput, key.KeyPair.PrivateKeyPkcs8);

        RrSigRecordData signed = new(
            unsigned.TypeCovered,
            unsigned.Algorithm,
            unsigned.Labels,
            unsigned.OriginalTimeToLiveSeconds,
            unsigned.SignatureExpiration,
            unsigned.SignatureInception,
            unsigned.KeyTag,
            unsigned.SignerName,
            signature
        );

        return new DnsResourceRecord(
            first.Name,
            DnsResourceRecordTypeEnum.RrSig,
            first.Class,
            first.TimeToLive,
            DnsRecordDataCodecRegistry.Encode(DnsResourceRecordTypeEnum.RrSig, signed)
        );
    }

    /// <summary>
    ///     This method builds the DS record a parent zone would publish for a child's zone key.
    /// </summary>
    public DnsResourceRecord BuildDs(DnsName childApex, ZoneKey childKey, uint ttl = 3600)
    {
        // Digest type 2 is SHA-256, per the IANA "Delegation Signer (DS) Resource Record (RR)
        // Type Digest Algorithms" registry -- the one digest type every real validator this
        // codebase targets is required to support.
        byte[] digest = DsDigestCodec.Compute(2, childApex, childKey.DnsKeyRecord.Data);
        DsRecordData ds = new(childKey.KeyTag, DnssecAlgorithmEnum.Ed25519, 2, digest);

        return new DnsResourceRecord(
            childApex,
            DnsResourceRecordTypeEnum.DS,
            DnsClassEnum.Internet,
            ttl,
            DnsRecordDataCodecRegistry.Encode(DnsResourceRecordTypeEnum.DS, ds)
        );
    }

    /// <summary>
    ///     This method builds a <see cref="TrustAnchor" /> matching a zone key, for a fake
    ///     <see cref="ITrustAnchorStore" /> to hand back as the root of trust.
    /// </summary>
    public TrustAnchor BuildTrustAnchor(DnsName owner, ZoneKey key)
    {
        byte[] digest = DsDigestCodec.Compute(2, owner, key.DnsKeyRecord.Data);
        return new TrustAnchor(key.KeyTag, DnssecAlgorithmEnum.Ed25519, 2, digest);
    }
}
