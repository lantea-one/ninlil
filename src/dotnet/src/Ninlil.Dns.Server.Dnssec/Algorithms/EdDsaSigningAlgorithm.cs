using Ninlil.Dns.Common.Enum;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Security;

// Define our namespace.
namespace Ninlil.Dns.Server.Dnssec.Algorithms;

/// <summary>
///     This class implements the two EdDSA-based DNSSEC algorithms this project supports --
///     <see cref="DnssecAlgorithmEnum.Ed25519" /> and <see cref="DnssecAlgorithmEnum.Ed448" />,
///     both defined by RFC 8080. Unlike every other algorithm this project implements, EdDSA has
///     no .NET BCL support at all as of .NET 10, so this class is the one place
///     <c>BouncyCastle.Cryptography</c> is used. RFC 8080 §3 gives both the same trivial wire
///     format the underlying algorithm itself already defines: the public key and signature are
///     each simply the algorithm's own raw encoded form, with no DNS-specific framing at all.
/// </summary>
/// <param name="algorithmNumber">Which of the two EdDSA-family algorithm numbers this instance is.</param>
/// <param name="variant">The concrete Ed25519 or Ed448 operations this algorithm number uses.</param>
public class EdDsaSigningAlgorithm(DnssecAlgorithmEnum algorithmNumber, IEdDsaVariant variant)
    : IDnssecSigningAlgorithm
{
    /// <inheritdoc />
    public DnssecAlgorithmEnum AlgorithmNumber { get; } = algorithmNumber;

    /// <inheritdoc />
    public DnssecKeyPair GenerateKeyPair(int keySizeBits = 0)
    {
        AsymmetricCipherKeyPair keyPair = variant.GenerateKeyPair(new SecureRandom());

        return new DnssecKeyPair(
            variant.EncodePublicKey(keyPair.Public),
            variant.EncodePrivateKeyPkcs8(keyPair.Private)
        );
    }

    /// <inheritdoc />
    public byte[] Sign(byte[] signingInput, byte[] privateKeyPkcs8)
    {
        ISigner signer = variant.CreateSigner();
        signer.Init(true, variant.DecodePrivateKeyPkcs8(privateKeyPkcs8));
        signer.BlockUpdate(signingInput, 0, signingInput.Length);

        return signer.GenerateSignature();
    }

    /// <inheritdoc />
    public bool Verify(byte[] signingInput, byte[] signature, byte[] publicKeyWireFormat)
    {
        ISigner signer = variant.CreateSigner();
        signer.Init(false, variant.DecodePublicKey(publicKeyWireFormat));
        signer.BlockUpdate(signingInput, 0, signingInput.Length);

        return signer.VerifySignature(signature);
    }
}
