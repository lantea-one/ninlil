using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Dnssec;
using Ninlil.Dns.Server.Dnssec.Algorithms;
using Ninlil.Dns.Server.Recursive.Validation;

namespace Ninlil.Dns.Server.Recursive.Tests.Support;

/// <summary>
///     This class builds cryptographically valid DNSKEY, DS and RRSIG records for tests that walk
///     a chain of trust, so verification is not faked. It uses Ed25519 throughout: no key size to
///     choose, and cheap.
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
    ///     This method generates a new zone key and its DNSKEY record.
    /// </summary>
    /// <param name="owner">The key's owner name.</param>
    /// <param name="ttl">The DNSKEY record's TTL.</param>
    /// <param name="isSecureEntryPoint">
    ///     Whether the key's SEP (KSK) bit is set. Off by default; a trust anchor rollover test
    ///     sets it, since only SEP keys are rollover candidates (RFC 5011).
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

        // The key tag is computed over the encoded RDATA, as validators and signers compute it.
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
        DateTimeOffset? expiration = null,
        byte? labels = null
    )
    {
        DnsResourceRecord first = rrset[0];
        // The default validity window opened a day ago and lasts a month, so ordinary tests need
        // not think about clocks; a test that needs an expired or future signature passes its own
        // times.
        DateTimeOffset actualInception = inception ?? DateTimeOffset.UtcNow.AddDays(-1);
        DateTimeOffset actualExpiration = expiration ?? DateTimeOffset.UtcNow.AddDays(30);

        RrSigRecordData unsigned = new(
            first.Type,
            DnssecAlgorithmEnum.Ed25519,
            labels ?? (byte)first.Name.Labels.Count,
            first.TimeToLive,
            (uint)actualExpiration.ToUnixTimeSeconds(),
            (uint)actualInception.ToUnixTimeSeconds(),
            key.KeyTag,
            signerName,
            []
        );

        // The RFC 4034 canonical form (lowercased owner names, sorted RRset, TTLs set to the
        // RRSIG's original TTL) is rebuilt here as a validator would, or signatures would verify only
        // by accident.
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
        // Digest type 2 is SHA-256, which every validator must support.
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
