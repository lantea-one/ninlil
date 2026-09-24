using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Dnssec.Algorithms;

/// <summary>
///     This class carries a freshly generated key pair's public and private halves, in the two
///     forms the rest of this project needs them in: <see cref="PublicKeyWireFormat" /> is ready
///     to embed directly in a <see cref="Common.Wire.Records.DnsKeyRecordData.PublicKey" />;
///     <see cref="PrivateKeyPkcs8" /> is the algorithm-agnostic PKCS#8 encoding every
///     <see cref="IDnssecSigningAlgorithm" /> implementation both produces and accepts, so key
///     storage never needs to know which specific algorithm a stored key belongs to.
/// </summary>
/// <param name="PublicKeyWireFormat">The public key, encoded per the algorithm's own RFC.</param>
/// <param name="PrivateKeyPkcs8">The private key, encoded as a PKCS#8 <c>PrivateKeyInfo</c>.</param>
public record DnssecKeyPair(byte[] PublicKeyWireFormat, byte[] PrivateKeyPkcs8);

/// <summary>
///     This interface is the one abstraction every DNSSEC signing algorithm this project supports
///     implements, letting <c>Server.Authoritative</c>'s signing pipeline and key management work
///     against a single shape regardless of whether a given
///     <see cref="DnssecAlgorithmEnum" /> value is backed by the .NET BCL's own
///     <see cref="System.Security.Cryptography.RSA" />/<see cref="System.Security.Cryptography.DSA" />/
///     <see cref="System.Security.Cryptography.ECDsa" />, or by BouncyCastle for the two EdDSA
///     algorithms the BCL has no support for.
/// </summary>
public interface IDnssecSigningAlgorithm
{
    /// <summary>
    ///     This property contains which <see cref="DnssecAlgorithmEnum" /> value this
    ///     implementation signs and verifies for.
    /// </summary>
    DnssecAlgorithmEnum AlgorithmNumber { get; }

    /// <summary>
    ///     This method generates a brand-new key pair.
    /// </summary>
    /// <param name="keySizeBits">
    ///     The key size to generate, in bits -- meaningful only for the RSA family, where
    ///     <c>0</c> requests this implementation's own default. Every other algorithm's key size
    ///     is fixed by the algorithm itself (DSA always generates the maximum, most
    ///     interoperable RFC 2536 size; ECDSA and EdDSA have no size to choose at all), so this
    ///     parameter is silently ignored by every implementation other than
    ///     <see cref="RsaSigningAlgorithm" />.
    /// </param>
    /// <returns>The generated key pair.</returns>
    DnssecKeyPair GenerateKeyPair(int keySizeBits = 0);

    /// <summary>
    ///     This method signs a signing input (see
    ///     <see cref="Common.Wire.DnssecCanonicalization.BuildSigningInput" />) with a private
    ///     key.
    /// </summary>
    /// <param name="signingInput">The exact bytes to sign.</param>
    /// <param name="privateKeyPkcs8">The signing private key, as produced by <see cref="GenerateKeyPair" />.</param>
    /// <returns>
    ///     The signature, encoded exactly as this algorithm's defining RFC requires for the
    ///     <see cref="Common.Wire.Records.RrSigRecordData.Signature" /> field.
    /// </returns>
    byte[] Sign(byte[] signingInput, byte[] privateKeyPkcs8);

    /// <summary>
    ///     This method verifies a signature against a signing input and public key.
    /// </summary>
    /// <param name="signingInput">The exact bytes that were signed.</param>
    /// <param name="signature">The signature to verify.</param>
    /// <param name="publicKeyWireFormat">
    ///     The verifying public key, in the same wire format <see cref="GenerateKeyPair" />
    ///     produces and <see cref="Common.Wire.Records.DnsKeyRecordData.PublicKey" /> stores it in.
    /// </param>
    /// <returns>A <see langword="bool" /> indicating whether the signature is valid.</returns>
    bool Verify(byte[] signingInput, byte[] signature, byte[] publicKeyWireFormat);
}
