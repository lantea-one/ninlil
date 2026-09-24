using System.Text;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Dnssec.Algorithms;

// Define our namespace.
namespace Ninlil.Dns.Server.Dnssec.Tests;

/// <summary>
///     This class tests every algorithm <see cref="DnssecAlgorithmRegistry" /> registers: a real
///     generate-key/sign/verify round trip, tamper detection, and cross-key rejection.
/// </summary>
public class DnssecAlgorithmRegistryTests
{
    public static readonly TheoryData<DnssecAlgorithmEnum> SupportedAlgorithms = new(
        DnssecAlgorithmEnum.RsaMd5,
        DnssecAlgorithmEnum.Dsa,
        DnssecAlgorithmEnum.RsaSha1,
        DnssecAlgorithmEnum.DsaNsec3Sha1,
        DnssecAlgorithmEnum.RsaSha1Nsec3Sha1,
        DnssecAlgorithmEnum.RsaSha256,
        DnssecAlgorithmEnum.RsaSha512,
        DnssecAlgorithmEnum.EcdsaP256Sha256,
        DnssecAlgorithmEnum.EcdsaP384Sha384,
        DnssecAlgorithmEnum.Ed25519,
        DnssecAlgorithmEnum.Ed448
    );

    [Theory]
    [MemberData(nameof(SupportedAlgorithms))]
    public void SignThenVerifySucceeds(DnssecAlgorithmEnum algorithmNumber)
    {
        Assert.True(
            DnssecAlgorithmRegistry.TryGetAlgorithm(
                algorithmNumber,
                out IDnssecSigningAlgorithm algorithm
            )
        );

        DnssecKeyPair keyPair = algorithm.GenerateKeyPair();
        byte[] signingInput = Encoding.ASCII.GetBytes($"{algorithmNumber} signing input");
        byte[] signature = algorithm.Sign(signingInput, keyPair.PrivateKeyPkcs8);

        Assert.True(algorithm.Verify(signingInput, signature, keyPair.PublicKeyWireFormat));
    }

    [Theory]
    [MemberData(nameof(SupportedAlgorithms))]
    public void VerifyFailsAgainstATamperedSigningInput(DnssecAlgorithmEnum algorithmNumber)
    {
        DnssecAlgorithmRegistry.TryGetAlgorithm(
            algorithmNumber,
            out IDnssecSigningAlgorithm algorithm
        );
        DnssecKeyPair keyPair = algorithm.GenerateKeyPair();
        byte[] signingInput = Encoding.ASCII.GetBytes($"{algorithmNumber} signing input");
        byte[] signature = algorithm.Sign(signingInput, keyPair.PrivateKeyPkcs8);

        byte[] tamperedInput = Encoding.ASCII.GetBytes($"{algorithmNumber} signing INPUT");

        Assert.False(algorithm.Verify(tamperedInput, signature, keyPair.PublicKeyWireFormat));
    }

    [Theory]
    [MemberData(nameof(SupportedAlgorithms))]
    public void VerifyFailsAgainstATamperedSignature(DnssecAlgorithmEnum algorithmNumber)
    {
        DnssecAlgorithmRegistry.TryGetAlgorithm(
            algorithmNumber,
            out IDnssecSigningAlgorithm algorithm
        );
        DnssecKeyPair keyPair = algorithm.GenerateKeyPair();
        byte[] signingInput = Encoding.ASCII.GetBytes($"{algorithmNumber} signing input");
        byte[] signature = algorithm.Sign(signingInput, keyPair.PrivateKeyPkcs8);

        byte[] tamperedSignature = [.. signature];
        tamperedSignature[^1] ^= 0xFF;

        Assert.False(
            algorithm.Verify(signingInput, tamperedSignature, keyPair.PublicKeyWireFormat)
        );
    }

    [Theory]
    [MemberData(nameof(SupportedAlgorithms))]
    public void VerifyFailsAgainstADifferentKeyPairsPublicKey(DnssecAlgorithmEnum algorithmNumber)
    {
        DnssecAlgorithmRegistry.TryGetAlgorithm(
            algorithmNumber,
            out IDnssecSigningAlgorithm algorithm
        );
        DnssecKeyPair keyPair = algorithm.GenerateKeyPair();
        DnssecKeyPair otherKeyPair = algorithm.GenerateKeyPair();
        byte[] signingInput = Encoding.ASCII.GetBytes($"{algorithmNumber} signing input");
        byte[] signature = algorithm.Sign(signingInput, keyPair.PrivateKeyPkcs8);

        Assert.False(algorithm.Verify(signingInput, signature, otherKeyPair.PublicKeyWireFormat));
    }

    [Fact]
    public void EccGostIsNotRegistered()
    {
        // Excluded per the flagged-infeasibility decision: no well-maintained, security-audited
        // .NET implementation exists for GOST curves.
        Assert.False(DnssecAlgorithmRegistry.TryGetAlgorithm(DnssecAlgorithmEnum.EccGost, out _));
    }

    [Fact]
    public void EcdsaP256PublicKeyIsExactly64Bytes()
    {
        DnssecAlgorithmRegistry.TryGetAlgorithm(
            DnssecAlgorithmEnum.EcdsaP256Sha256,
            out IDnssecSigningAlgorithm algorithm
        );

        Assert.Equal(64, algorithm.GenerateKeyPair().PublicKeyWireFormat.Length);
    }

    [Fact]
    public void EcdsaP384PublicKeyIsExactly96Bytes()
    {
        DnssecAlgorithmRegistry.TryGetAlgorithm(
            DnssecAlgorithmEnum.EcdsaP384Sha384,
            out IDnssecSigningAlgorithm algorithm
        );

        Assert.Equal(96, algorithm.GenerateKeyPair().PublicKeyWireFormat.Length);
    }

    [Fact]
    public void Ed25519PublicKeyIsExactly32Bytes()
    {
        DnssecAlgorithmRegistry.TryGetAlgorithm(
            DnssecAlgorithmEnum.Ed25519,
            out IDnssecSigningAlgorithm algorithm
        );

        Assert.Equal(32, algorithm.GenerateKeyPair().PublicKeyWireFormat.Length);
    }

    [Fact]
    public void Ed448PublicKeyIsExactly57Bytes()
    {
        DnssecAlgorithmRegistry.TryGetAlgorithm(
            DnssecAlgorithmEnum.Ed448,
            out IDnssecSigningAlgorithm algorithm
        );

        Assert.Equal(57, algorithm.GenerateKeyPair().PublicKeyWireFormat.Length);
    }

    [Fact]
    public void DsaPublicKeyMatchesRfc2536T8ShapeFor1024BitKey()
    {
        DnssecAlgorithmRegistry.TryGetAlgorithm(
            DnssecAlgorithmEnum.Dsa,
            out IDnssecSigningAlgorithm algorithm
        );

        DnssecKeyPair keyPair = algorithm.GenerateKeyPair();

        // T=8 -> 1 (T byte) + 20 (Q) + 3 * 128 (P, G, Y, each 64+8*8=128 bytes) = 405 bytes.
        Assert.Equal(1 + 20 + 3 * 128, keyPair.PublicKeyWireFormat.Length);
        Assert.Equal(8, keyPair.PublicKeyWireFormat[0]);
    }

    [Fact]
    public void DsaSignatureMatchesRfc2536ShapeAndSharesThePublicKeysTByte()
    {
        DnssecAlgorithmRegistry.TryGetAlgorithm(
            DnssecAlgorithmEnum.Dsa,
            out IDnssecSigningAlgorithm algorithm
        );
        DnssecKeyPair keyPair = algorithm.GenerateKeyPair();

        byte[] signature = algorithm.Sign(Encoding.ASCII.GetBytes("dsa"), keyPair.PrivateKeyPkcs8);

        // T=1 + R=20 + S=20 = 41 bytes.
        Assert.Equal(41, signature.Length);
        Assert.Equal(keyPair.PublicKeyWireFormat[0], signature[0]);
    }
}
