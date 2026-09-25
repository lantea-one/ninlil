using System.Security.Cryptography;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.DnsCrypt;
using Ninlil.Dns.Server.Dnssec.Algorithms;

// Define our namespace.
namespace Ninlil.Dns.Server.DnsCrypt.Tests;

/// <summary>
///     This class tests <see cref="DnsCryptCertificateCodec" /> with certificates signed by a
///     newly generated Ed25519 provider key: the encoded length, and that a signature over
///     <see cref="DnsCryptCertificateCodec.BuildSignedData" /> verifies and fails when the
///     signed bytes are changed. <see cref="DnsCryptCertificateCodec.Decode" /> is not covered
///     here.
/// </summary>
public class DnsCryptCertificateCodecTests
{
    /// <summary>
    ///     This method builds a signed <see cref="DnsCryptCertificate" /> valid for 48 hours from
    ///     new Ed25519 provider and X25519 resolver key pairs, signing the output of
    ///     <see cref="DnsCryptCertificateCodec.BuildSignedData" />. It returns the certificate with
    ///     the signed bytes, algorithm and provider key pair, so a test can verify the signature
    ///     itself.
    /// </summary>
    private static (
        DnsCryptCertificate Signed,
        byte[] SigningInput,
        IDnssecSigningAlgorithm Algorithm,
        DnssecKeyPair ProviderKeyPair
    ) BuildSignedCertificate()
    {
        Assert.True(
            DnssecAlgorithmRegistry.TryGetAlgorithm(
                DnssecAlgorithmEnum.Ed25519,
                out IDnssecSigningAlgorithm algorithm
            )
        );

        DnssecKeyPair providerKeyPair = algorithm.GenerateKeyPair();
        (byte[] resolverPublicKey, _) = DnsCryptCryptoBox.GenerateKeyPair(
            DnsCryptEsVersion.XChaCha20Poly1305
        );

        DateTimeOffset now = DateTimeOffset.UtcNow;
        DnsCryptCertificate unsigned = new(
            DnsCryptEsVersion.XChaCha20Poly1305,
            resolverPublicKey,
            RandomNumberGenerator.GetBytes(8),
            1,
            now,
            now + TimeSpan.FromHours(48),
            []
        );

        byte[] signingInput = DnsCryptCertificateCodec.BuildSignedData(unsigned);
        byte[] signature = algorithm.Sign(signingInput, providerKeyPair.PrivateKeyPkcs8);
        DnsCryptCertificate signed = unsigned with { Signature = signature };

        return (signed, signingInput, algorithm, providerKeyPair);
    }

    [Fact]
    public void Encode_ProducesExactly124Bytes()
    {
        (DnsCryptCertificate signed, _, _, _) = BuildSignedCertificate();

        byte[] encoded = DnsCryptCertificateCodec.Encode(signed);

        Assert.Equal(124, encoded.Length);
    }

    [Fact]
    public void Verify_AcceptsACorrectlySignedCertificate()
    {
        (
            DnsCryptCertificate signed,
            byte[] signingInput,
            IDnssecSigningAlgorithm algorithm,
            DnssecKeyPair providerKeyPair
        ) = BuildSignedCertificate();

        bool verified = algorithm.Verify(
            signingInput,
            signed.Signature,
            providerKeyPair.PublicKeyWireFormat
        );

        Assert.True(verified);
    }

    [Fact]
    public void Verify_RejectsATamperedSigningInput()
    {
        (
            DnsCryptCertificate signed,
            byte[] signingInput,
            IDnssecSigningAlgorithm algorithm,
            DnssecKeyPair providerKeyPair
        ) = BuildSignedCertificate();

        byte[] tamperedInput = (byte[])signingInput.Clone();
        tamperedInput[0] ^= 0xFF;

        bool verified = algorithm.Verify(
            tamperedInput,
            signed.Signature,
            providerKeyPair.PublicKeyWireFormat
        );

        Assert.False(verified);
    }
}
