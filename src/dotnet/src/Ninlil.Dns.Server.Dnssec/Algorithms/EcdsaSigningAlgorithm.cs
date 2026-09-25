using System.Security.Cryptography;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Dnssec.Algorithms;

/// <summary>
///     This class implements the ECDSA DNSSEC algorithms from RFC 6605,
///     <see cref="DnssecAlgorithmEnum.EcdsaP256Sha256" /> and <see cref="DnssecAlgorithmEnum.EcdsaP384Sha384" />,
///     which differ only in curve and hash. Per RFC 6605 §4, the public key is the point's X and
///     Y coordinates concatenated, with no compression flag or length prefix, and the signature is
///     R and S concatenated. That signature layout is the .NET IEEE P1363 fixed-field format, so
///     no conversion is needed.
/// </summary>
/// <param name="algorithmNumber">Which of the two ECDSA algorithm numbers this instance handles.</param>
/// <param name="curve">The curve this algorithm number uses.</param>
/// <param name="hashAlgorithm">The hash algorithm this algorithm number signs with.</param>
/// <param name="fieldSizeBytes">
///     The curve's field element size in octets (32 for P-256, 48 for P-384), used to split the
///     public key into its X and Y coordinates.
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
