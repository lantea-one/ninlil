using System.Security.Cryptography;
using Ninlil.Dns.Server.DnsCrypt;

// Define our namespace.
namespace Ninlil.Dns.Server.DnsCrypt.Tests;

public class DnsCryptCryptoBoxTests
{
    private static readonly byte[] Message =
        "the quick brown fox jumps over the lazy dog"u8.ToArray();

    [Theory]
    [InlineData(DnsCryptEsVersion.XSalsa20Poly1305)]
    [InlineData(DnsCryptEsVersion.XChaCha20Poly1305)]
    public void EncryptThenDecrypt_RecoversTheOriginalMessage(DnsCryptEsVersion esVersion)
    {
        (byte[] serverPublicKey, byte[] serverSecretKey) = DnsCryptCryptoBox.GenerateKeyPair(
            esVersion
        );
        (byte[] clientPublicKey, byte[] clientSecretKey) = DnsCryptCryptoBox.GenerateKeyPair(
            esVersion
        );
        byte[] nonce = DnsCryptCryptoBox.GenerateNonce();

        byte[] cipherText = DnsCryptCryptoBox.Encrypt(
            esVersion,
            Message,
            nonce,
            serverPublicKey,
            clientSecretKey
        );
        byte[] plainText = DnsCryptCryptoBox.Decrypt(
            esVersion,
            cipherText,
            nonce,
            clientPublicKey,
            serverSecretKey
        );

        Assert.Equal(Message, plainText);
    }

    [Theory]
    [InlineData(DnsCryptEsVersion.XSalsa20Poly1305)]
    [InlineData(DnsCryptEsVersion.XChaCha20Poly1305)]
    public void Decrypt_RejectsATamperedCipherText(DnsCryptEsVersion esVersion)
    {
        (byte[] serverPublicKey, byte[] serverSecretKey) = DnsCryptCryptoBox.GenerateKeyPair(
            esVersion
        );
        (byte[] clientPublicKey, byte[] clientSecretKey) = DnsCryptCryptoBox.GenerateKeyPair(
            esVersion
        );
        byte[] nonce = DnsCryptCryptoBox.GenerateNonce();

        byte[] cipherText = DnsCryptCryptoBox.Encrypt(
            esVersion,
            Message,
            nonce,
            serverPublicKey,
            clientSecretKey
        );
        byte[] tampered = (byte[])cipherText.Clone();
        tampered[0] ^= 0xFF;

        Assert.Throws<CryptographicException>(() =>
            DnsCryptCryptoBox.Decrypt(esVersion, tampered, nonce, clientPublicKey, serverSecretKey)
        );
    }
}
