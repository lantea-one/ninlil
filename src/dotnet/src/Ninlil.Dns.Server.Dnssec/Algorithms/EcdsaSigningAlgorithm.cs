using System.Security.Cryptography;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Dnssec.Algorithms;

/// <summary>
///     This class implements the two ECDSA-based DNSSEC algorithms this project supports --
///     <see cref="DnssecAlgorithmEnum.EcdsaP256Sha256" /> and <see cref="DnssecAlgorithmEnum.EcdsaP384Sha384" />,
///     both defined by RFC 6605 -- which differ only in curve and matching hash algorithm. RFC
///     6605 §4 gives both the same, much simpler wire format than the RSA and DSA families use:
///     the public key is the curve point's two coordinates concatenated with no compression, sign,
///     or length prefix at all, and the signature is the two integers R and S concatenated the
///     same way -- exactly what .NET's own IEEE P1363 fixed-field format already produces, with
///     no DNS-specific translation needed.
/// </summary>
/// <param name="algorithmNumber">Which of the two ECDSA-family algorithm numbers this instance is.</param>
/// <param name="curve">The curve this algorithm number uses.</param>
/// <param name="hashAlgorithm">The hash algorithm this algorithm number signs with.</param>
/// <param name="fieldSizeBytes">
///     The curve's field element size, in octets -- 32 for P-256, 48 for P-384 -- used to split
///     the public key's concatenated coordinates back apart on decode.
/// </param>
public class EcdsaSigningAlgorithm(
    DnssecAlgorithmEnum algorithmNumber,
    ECCurve curve,
    HashAlgorithmName hashAlgorithm,
    int fieldSizeBytes
) : IDnssecSigningAlgorithm
{
    /// <inheritdoc />
    public DnssecAlgorithmEnum AlgorithmNumber { get; } = algorithmNumber;

    /// <inheritdoc />
    public DnssecKeyPair GenerateKeyPair(int keySizeBits = 0)
    {
        using ECDsa ecdsa = ECDsa.Create(curve);
        ECParameters parameters = ecdsa.ExportParameters(false);

        return new DnssecKeyPair(
            [.. parameters.Q.X, .. parameters.Q.Y],
            ecdsa.ExportPkcs8PrivateKey()
        );
    }

    /// <inheritdoc />
    public byte[] Sign(byte[] signingInput, byte[] privateKeyPkcs8)
    {
        using ECDsa ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(privateKeyPkcs8, out _);

        return ecdsa.SignData(
            signingInput,
            hashAlgorithm,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation
        );
    }

    /// <inheritdoc />
    public bool Verify(byte[] signingInput, byte[] signature, byte[] publicKeyWireFormat)
    {
        using ECDsa ecdsa = ECDsa.Create();
        ecdsa.ImportParameters(
            new ECParameters
            {
                Curve = curve,
                Q = new ECPoint
                {
                    X = publicKeyWireFormat[..fieldSizeBytes],
                    Y = publicKeyWireFormat[fieldSizeBytes..],
                },
            }
        );

        return ecdsa.VerifyData(
            signingInput,
            signature,
            hashAlgorithm,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation
        );
    }
}
