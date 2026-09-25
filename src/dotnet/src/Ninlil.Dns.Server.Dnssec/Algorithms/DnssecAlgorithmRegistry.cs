using System.Security.Cryptography;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Dnssec.Algorithms;

/// <summary>
///     This class maps each <see cref="DnssecAlgorithmEnum" /> value to its
///     <see cref="IDnssecSigningAlgorithm" />. It covers the signing algorithms 1, 3, 5, 6, 7, 8,
///     10, 13, 14, 15 and 16. Algorithm 12 (ECC-GOST) is left out because there is no
///     well-maintained, audited .NET implementation. DS and DNSKEY records that use algorithm 12
///     can still be parsed and stored; they just cannot be signed or verified.
///     <para>
///         Nothing here excludes the algorithms RFC 8624 says must not be used for signing
///         (RSAMD5, DSA, DSA-NSEC3-SHA1). Key generation limits them only through
///         <see cref="DnssecAlgorithmPolicyOptions" />.
///     </para>
/// </summary>
public static class DnssecAlgorithmRegistry
{
    /// <summary>
    ///     This field contains every supported algorithm, keyed by its <see cref="DnssecAlgorithmEnum" />
    ///     value.
    /// </summary>
    private static readonly Dictionary<DnssecAlgorithmEnum, IDnssecSigningAlgorithm> Algorithms =
        new()
        {
            [DnssecAlgorithmEnum.RsaMd5] = new RsaSigningAlgorithm(
                DnssecAlgorithmEnum.RsaMd5,
                HashAlgorithmName.MD5
            ),
            [DnssecAlgorithmEnum.Dsa] = new DsaSigningAlgorithm(DnssecAlgorithmEnum.Dsa),
            [DnssecAlgorithmEnum.RsaSha1] = new RsaSigningAlgorithm(
                DnssecAlgorithmEnum.RsaSha1,
                HashAlgorithmName.SHA1
            ),
            [DnssecAlgorithmEnum.DsaNsec3Sha1] = new DsaSigningAlgorithm(
                DnssecAlgorithmEnum.DsaNsec3Sha1
            ),
            [DnssecAlgorithmEnum.RsaSha1Nsec3Sha1] = new RsaSigningAlgorithm(
                DnssecAlgorithmEnum.RsaSha1Nsec3Sha1,
                HashAlgorithmName.SHA1
            ),
            [DnssecAlgorithmEnum.RsaSha256] = new RsaSigningAlgorithm(
                DnssecAlgorithmEnum.RsaSha256,
                HashAlgorithmName.SHA256
            ),
            [DnssecAlgorithmEnum.RsaSha512] = new RsaSigningAlgorithm(
                DnssecAlgorithmEnum.RsaSha512,
                HashAlgorithmName.SHA512
            ),
            [DnssecAlgorithmEnum.EcdsaP256Sha256] = new EcdsaSigningAlgorithm(
                DnssecAlgorithmEnum.EcdsaP256Sha256,
                ECCurve.NamedCurves.nistP256,
                HashAlgorithmName.SHA256,
                32
            ),
            [DnssecAlgorithmEnum.EcdsaP384Sha384] = new EcdsaSigningAlgorithm(
                DnssecAlgorithmEnum.EcdsaP384Sha384,
                ECCurve.NamedCurves.nistP384,
                HashAlgorithmName.SHA384,
                48
            ),
            [DnssecAlgorithmEnum.Ed25519] = new EdDsaSigningAlgorithm(
                DnssecAlgorithmEnum.Ed25519,
                new Ed25519Variant()
            ),
            [DnssecAlgorithmEnum.Ed448] = new EdDsaSigningAlgorithm(
                DnssecAlgorithmEnum.Ed448,
                new Ed448Variant()
            ),
        };

    /// <summary>
    ///     This method looks up the signing algorithm implementation registered for a
    ///     <see cref="DnssecAlgorithmEnum" /> value.
    /// </summary>
    /// <param name="algorithmNumber">The algorithm to look up.</param>
    /// <param name="algorithm">The registered implementation, if one exists.</param>
    /// <returns>A <see langword="bool" /> indicating whether an implementation was found.</returns>
    public static bool TryGetAlgorithm(
        DnssecAlgorithmEnum algorithmNumber,
        out IDnssecSigningAlgorithm algorithm
    ) => Algorithms.TryGetValue(algorithmNumber, out algorithm);
}
