using System.Security.Cryptography;
using Ninlil.Dns.Server.DnsCrypt;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsCrypt.Tests;

/// <summary>
///     This class checks the response-size arithmetic of
///     <see cref="DnsCryptQueryHandler.MaxPlaintextResponseLength" /> against the real encryption
///     of <see cref="DnsCryptEnvelopeCodec.EncryptResponse" />: a plaintext of the budget fits in
///     a packet the size of the query, and one byte more does not.
/// </summary>
public sealed class DnsCryptResponseBudgetTests
{
    [Theory]
    [InlineData(DnsCryptEsVersion.XChaCha20Poly1305, 256)]
    [InlineData(DnsCryptEsVersion.XChaCha20Poly1305, 300)]
    [InlineData(DnsCryptEsVersion.XSalsa20Poly1305, 1024)]
    [InlineData(DnsCryptEsVersion.XSalsa20Poly1305, 132)]
    public void ThePlaintextBudgetIsTheLargestResponseThatEncryptsToAtMostTheQueryLength(
        DnsCryptEsVersion esVersion,
        int queryPacketLength
    )
    {
        // The envelope codec reads only the cipher and the resolver secret key from the entry,
        // so the certificate needs no valid signature. The listener fixture is not used here: it
        // binds a fixed port, which a second fixture instance would fight over.
        (byte[] resolverPublicKey, byte[] resolverSecretKey) = DnsCryptCryptoBox.GenerateKeyPair(
            esVersion
        );
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DnsCryptCertificateEntry certificateEntry = new(
            new DnsCryptCertificate(
                esVersion,
                resolverPublicKey,
                RandomNumberGenerator.GetBytes(8),
                1,
                now,
                now.AddHours(1),
                []
            ),
            resolverSecretKey
        );
        (byte[] clientPublicKey, _) = DnsCryptCryptoBox.GenerateKeyPair(esVersion);
        byte[] clientNonceHalf = RandomNumberGenerator.GetBytes(
            DnsCryptEnvelopeCodec.HalfNonceLength
        );

        int budget = DnsCryptQueryHandler.MaxPlaintextResponseLength(queryPacketLength);

        byte[] atBudget = DnsCryptEnvelopeCodec.EncryptResponse(
            certificateEntry,
            clientPublicKey,
            clientNonceHalf,
            new byte[budget]
        );
        byte[] oneOver = DnsCryptEnvelopeCodec.EncryptResponse(
            certificateEntry,
            clientPublicKey,
            clientNonceHalf,
            new byte[budget + 1]
        );

        Assert.True(atBudget.Length <= queryPacketLength);
        Assert.True(oneOver.Length > queryPacketLength);
    }

    [Theory]
    [InlineData(256, 191)]
    [InlineData(300, 191)]
    [InlineData(1024, 959)]
    public void TheBudgetMatchesTheWorkedExample(int queryPacketLength, int expectedBudget) =>
        Assert.Equal(
            expectedBudget,
            DnsCryptQueryHandler.MaxPlaintextResponseLength(queryPacketLength)
        );

    [Theory]
    [InlineData(0)]
    [InlineData(69)]
    [InlineData(111)]
    public void AQueryTooShortForAnyResponseHasABudgetOfZero(int queryPacketLength) =>
        Assert.Equal(0, DnsCryptQueryHandler.MaxPlaintextResponseLength(queryPacketLength));
}
