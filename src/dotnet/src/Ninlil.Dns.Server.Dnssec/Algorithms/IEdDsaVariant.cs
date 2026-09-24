using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;

// Define our namespace.
namespace Ninlil.Dns.Server.Dnssec.Algorithms;

/// <summary>
///     This interface isolates the handful of BouncyCastle types and calls that actually differ
///     between Ed25519 and Ed448, so <see cref="EdDsaSigningAlgorithm" /> itself needs no
///     algorithm-specific branching at all.
/// </summary>
public interface IEdDsaVariant
{
    /// <summary>
    ///     This method generates a new key pair.
    /// </summary>
    AsymmetricCipherKeyPair GenerateKeyPair(SecureRandom random);

    /// <summary>
    ///     This method creates a fresh signer/verifier instance.
    /// </summary>
    ISigner CreateSigner();

    /// <summary>
    ///     This method encodes a public key in its raw, RFC 8080 wire form.
    /// </summary>
    byte[] EncodePublicKey(AsymmetricKeyParameter publicKey);

    /// <summary>
    ///     This method decodes a public key from its raw, RFC 8080 wire form.
    /// </summary>
    AsymmetricKeyParameter DecodePublicKey(byte[] wireFormat);

    /// <summary>
    ///     This method encodes a private key as a PKCS#8 <c>PrivateKeyInfo</c>.
    /// </summary>
    byte[] EncodePrivateKeyPkcs8(AsymmetricKeyParameter privateKey);

    /// <summary>
    ///     This method decodes a private key from a PKCS#8 <c>PrivateKeyInfo</c>.
    /// </summary>
    AsymmetricKeyParameter DecodePrivateKeyPkcs8(byte[] pkcs8);
}

/// <summary>
///     This class implements <see cref="IEdDsaVariant" /> for Ed25519.
/// </summary>
public sealed class Ed25519Variant : IEdDsaVariant
{
    /// <inheritdoc />
    public AsymmetricCipherKeyPair GenerateKeyPair(SecureRandom random)
    {
        Ed25519KeyPairGenerator generator = new();
        generator.Init(new Ed25519KeyGenerationParameters(random));

        return generator.GenerateKeyPair();
    }

    /// <inheritdoc />
    public ISigner CreateSigner() => new Ed25519Signer();

    /// <inheritdoc />
    public byte[] EncodePublicKey(AsymmetricKeyParameter publicKey) =>
        ((Ed25519PublicKeyParameters)publicKey).GetEncoded();

    /// <inheritdoc />
    public AsymmetricKeyParameter DecodePublicKey(byte[] wireFormat) =>
        new Ed25519PublicKeyParameters(wireFormat, 0);

    /// <inheritdoc />
    public byte[] EncodePrivateKeyPkcs8(AsymmetricKeyParameter privateKey) =>
        PrivateKeyInfoFactory.CreatePrivateKeyInfo(privateKey).GetDerEncoded();

    /// <inheritdoc />
    public AsymmetricKeyParameter DecodePrivateKeyPkcs8(byte[] pkcs8) =>
        PrivateKeyFactory.CreateKey(pkcs8);
}

/// <summary>
///     This class implements <see cref="IEdDsaVariant" /> for Ed448.
/// </summary>
public sealed class Ed448Variant : IEdDsaVariant
{
    /// <inheritdoc />
    public AsymmetricCipherKeyPair GenerateKeyPair(SecureRandom random)
    {
        Ed448KeyPairGenerator generator = new();
        generator.Init(new Ed448KeyGenerationParameters(random));

        return generator.GenerateKeyPair();
    }

    /// <inheritdoc />
    public ISigner CreateSigner() => new Ed448Signer([]);

    /// <inheritdoc />
    public byte[] EncodePublicKey(AsymmetricKeyParameter publicKey) =>
        ((Ed448PublicKeyParameters)publicKey).GetEncoded();

    /// <inheritdoc />
    public AsymmetricKeyParameter DecodePublicKey(byte[] wireFormat) =>
        new Ed448PublicKeyParameters(wireFormat, 0);

    /// <inheritdoc />
    public byte[] EncodePrivateKeyPkcs8(AsymmetricKeyParameter privateKey) =>
        PrivateKeyInfoFactory.CreatePrivateKeyInfo(privateKey).GetDerEncoded();

    /// <inheritdoc />
    public AsymmetricKeyParameter DecodePrivateKeyPkcs8(byte[] pkcs8) =>
        PrivateKeyFactory.CreateKey(pkcs8);
}
