using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Dnssec;

// Define our namespace.
namespace Ninlil.Web.Services;

/// <summary>
///     This class renders what an operator hands to a parent zone for one of their DNSSEC keys:
///     the DNSKEY record in presentation format, and the SHA-256 DS record derived from it (RFC
///     4034 §5.1.4, RFC 4509). Registrars differ in which of the two they accept, and those that
///     take DS usually ask for the digest on its own, so all three are returned.
/// </summary>
public static class DnssecParentRecords
{
    /// <summary>
    ///     This constant contains the DS digest type used: SHA-256, which every parent accepts
    ///     and RFC 8624 says must be implemented.
    /// </summary>
    private const byte Sha256DigestType = 2;

    /// <summary>
    ///     This method describes a key's parent-facing records. The DNSKEY RDATA is encoded the way
    ///     <c>ZoneSigningService</c> publishes it, so the DS digest matches what resolvers see in
    ///     the zone.
    /// </summary>
    /// <param name="zoneName">The zone the key belongs to.</param>
    /// <param name="role">The key's role; only a KSK gets a DS record, since only a KSK is referenced from the parent.</param>
    /// <param name="algorithm">The key's signing algorithm.</param>
    /// <param name="publicKey">The key's public key material.</param>
    /// <param name="keyTag">The key's key tag.</param>
    /// <returns>The DNSKEY record, and the DS record and its digest (both <see langword="null" /> for a ZSK).</returns>
    public static (string DnsKeyRecord, string DsRecord, string DsDigest) Describe(
        string zoneName,
        DnssecKeyRoleEnum role,
        DnssecAlgorithmEnum algorithm,
        byte[] publicKey,
        ushort keyTag
    )
    {
        DnsName owner = DnsName.Parse(zoneName);
        bool isKsk = role == DnssecKeyRoleEnum.Ksk;
        byte[] rdata = DnsRecordDataCodecRegistry.Encode(
            DnsResourceRecordTypeEnum.DnsKey,
            new DnsKeyRecordData(true, isKsk, algorithm, publicKey)
        );

        // Zone key flag (256), plus the Secure Entry Point flag (1) for a KSK.
        int flags = isKsk ? 257 : 256;
        string dnsKeyRecord =
            $"{owner} IN DNSKEY {flags} {DnsKeyRecordData.Protocol} {(byte)algorithm} "
            + Convert.ToBase64String(publicKey);

        if (!isKsk)
            return (dnsKeyRecord, null, null);

        string digest = Convert.ToHexString(DsDigestCodec.Compute(Sha256DigestType, owner, rdata));
        string dsRecord = $"{owner} IN DS {keyTag} {(byte)algorithm} {Sha256DigestType} {digest}";

        return (dnsKeyRecord, dsRecord, digest);
    }
}
