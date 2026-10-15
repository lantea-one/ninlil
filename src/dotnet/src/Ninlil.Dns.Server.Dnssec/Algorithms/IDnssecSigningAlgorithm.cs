using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Dnssec.Algorithms;

/// <summary>
///     This record carries a newly generated key pair. <see cref="PublicKeyWireFormat" /> goes
///     straight into <see cref="Common.Wire.Records.DnsKeyRecordData.PublicKey" />.
///     <see cref="PrivateKeyPkcs8" /> is PKCS#8, which every
///     <see cref="IDnssecSigningAlgorithm" /> produces and accepts, so key storage does not need
///     to know the algorithm.
/// </summary>
/// <param name="PublicKeyWireFormat">The public key, encoded as the algorithm's RFC defines.</param>
/// <param name="PrivateKeyPkcs8">The private key, encoded as a PKCS#8 <c>PrivateKeyInfo</c>.</param>
public record DnssecKeyPair(byte[] PublicKeyWireFormat, byte[] PrivateKeyPkcs8);

/// <summary>
///     This interface is implemented once per DNSSEC algorithm family, so key management and the
///     signing and validation code in <c>Server.Authoritative</c> and <c>Server.Recursive</c>
///     work the same whether an algorithm is backed by the .NET
///     <see cref="System.Security.Cryptography.RSA" />, <see cref="System.Security.Cryptography.DSA" />
///     and <see cref="System.Security.Cryptography.ECDsa" /> classes or by BouncyCastle (EdDSA).
///     Implementations are looked up through <see cref="DnssecAlgorithmRegistry" />.
/// </summary>
public interface IDnssecSigningAlgorithm
{
    /// <summary>
    ///     This property contains the algorithm number this instance signs and verifies for.
    /// </summary>
    DnssecAlgorithmEnum AlgorithmNumber { get; }

    /// <summary>
    ///     This method generates a new key pair.
    /// </summary>
    /// <param name="keySizeBits">
    ///     The key size in bits. Only <see cref="RsaSigningAlgorithm" /> uses it, where <c>0</c>
    ///     means its default and any other value is range-checked (see
    ///     <see cref="RsaSigningAlgorithm.IsValidKeySize" />). The others ignore it: DSA always
    ///     uses the largest RFC 2536 size, and ECDSA and EdDSA have fixed sizes.
    /// </param>
    /// <returns>The generated key pair.</returns>
    DnssecKeyPair GenerateKeyPair(int keySizeBits = 0);

    /// <summary>
    ///     This method signs a signing input (see
    ///     <see cref="Common.Wire.DnssecCanonicalization.BuildSigningInput" />) with a private
    ///     key.
    /// </summary>
    /// <param name="signingInput">The bytes to sign.</param>
    /// <param name="privateKeyPkcs8">The signing private key, as produced by <see cref="GenerateKeyPair" />.</param>
    /// <returns>
    ///     The signature, encoded as the algorithm's RFC requires for the
    ///     <see cref="Common.Wire.Records.RrSigRecordData.Signature" /> field.
    /// </returns>
    byte[] Sign(byte[] signingInput, byte[] privateKeyPkcs8);

    /// <summary>
    ///     This method verifies a signature against a signing input and public key.
    /// </summary>
    /// <param name="signingInput">The bytes that were signed.</param>
    /// <param name="signature">The signature to verify.</param>
    /// <param name="publicKeyWireFormat">
    ///     The verifying public key, in the same wire format <see cref="GenerateKeyPair" />
    ///     produces and <see cref="Common.Wire.Records.DnsKeyRecordData.PublicKey" /> stores it in.
    /// </param>
    /// <returns>A <see langword="bool" /> indicating whether the signature is valid.</returns>
    bool Verify(byte[] signingInput, byte[] signature, byte[] publicKeyWireFormat);
}
