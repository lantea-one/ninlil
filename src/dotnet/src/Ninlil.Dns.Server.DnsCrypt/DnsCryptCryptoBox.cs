using System.Security.Cryptography;
using Sodium;

// Define our namespace.
namespace Ninlil.Dns.Server.DnsCrypt;

/// <summary>
///     This class implements the public-key "box" encryption that DNSCrypt queries and
///     responses use, with one API for both <see cref="DnsCryptEsVersion" /> values:
///     <see cref="DnsCryptEsVersion.XSalsa20Poly1305" /> through the <see cref="PublicKeyBox" />
///     class of <c>Sodium.Core</c>, and <see cref="DnsCryptEsVersion.XChaCha20Poly1305" />
///     through the libsodium functions declared in <see cref="NativeLibsodium" />.
///     <see cref="DnsCryptEnvelopeCodec" />, <see cref="DnsCryptCertificateRotationService" /> and
///     the DNSCrypt forwarder client call it.
/// </summary>
public static class DnsCryptCryptoBox
{
    /// <summary>
    ///     This method generates a fresh X25519 key pair for a given cipher.
    /// </summary>
    public static (byte[] PublicKey, byte[] SecretKey) GenerateKeyPair(DnsCryptEsVersion esVersion)
    {
        switch (esVersion)
        {
            case DnsCryptEsVersion.XSalsa20Poly1305:
            {
                KeyPair pair = PublicKeyBox.GenerateKeyPair();
                return (pair.PublicKey, pair.PrivateKey);
            }
            case DnsCryptEsVersion.XChaCha20Poly1305:
            {
                byte[] publicKey = new byte[NativeLibsodium.PublicKeyBytes];
                byte[] secretKey = new byte[NativeLibsodium.SecretKeyBytes];

                if (
                    NativeLibsodium.crypto_box_curve25519xchacha20poly1305_keypair(
                        publicKey,
                        secretKey
                    ) != 0
                )
                    throw new CryptographicException(
                        "libsodium failed to generate an X25519 key pair."
                    );

                return (publicKey, secretKey);
            }
            default:
                throw new NotSupportedException(
                    $"DNSCrypt encryption system {esVersion} is not supported."
                );
        }
    }

    /// <summary>
    ///     This method generates a random 24-byte nonce, the nonce size of both ciphers.
    /// </summary>
    public static byte[] GenerateNonce() =>
        RandomNumberGenerator.GetBytes(NativeLibsodium.NonceBytes);

    /// <summary>
    ///     This method encrypts (and authenticates) a message for a recipient.
    /// </summary>
    /// <param name="esVersion">Which cipher to use.</param>
    /// <param name="message">The plaintext to encrypt.</param>
    /// <param name="nonce">A 24-byte nonce, unique for this sender/recipient key pair.</param>
    /// <param name="recipientPublicKey">The recipient's X25519 public key.</param>
    /// <param name="senderSecretKey">The sender's X25519 secret key.</param>
    /// <returns>The authentication tag followed by the ciphertext, the libsodium "easy" box layout and the DNSCrypt wire format.</returns>
    public static byte[] Encrypt(
        DnsCryptEsVersion esVersion,
        byte[] message,
        byte[] nonce,
        byte[] recipientPublicKey,
        byte[] senderSecretKey
    ) =>
        esVersion switch
        {
            DnsCryptEsVersion.XSalsa20Poly1305 => PublicKeyBox.Create(
                message,
                nonce,
                senderSecretKey,
                recipientPublicKey
            ),
            DnsCryptEsVersion.XChaCha20Poly1305 => EncryptXChaCha20Poly1305(
                message,
                nonce,
                recipientPublicKey,
                senderSecretKey
            ),
            _ => throw new NotSupportedException(
                $"DNSCrypt encryption system {esVersion} is not supported."
            ),
        };

    /// <summary>
    ///     This method decrypts and authenticates a message from a sender.
    /// </summary>
    /// <param name="esVersion">Which cipher to use.</param>
    /// <param name="cipherText">The authentication tag followed by the ciphertext.</param>
    /// <param name="nonce">The same nonce <see cref="Encrypt" /> was called with.</param>
    /// <param name="senderPublicKey">The sender's X25519 public key.</param>
    /// <param name="recipientSecretKey">The recipient's X25519 secret key.</param>
    /// <returns>The decrypted plaintext.</returns>
    /// <exception cref="CryptographicException">
    ///     Thrown when authentication fails: a tampered, corrupted or forged message.
    /// </exception>
    public static byte[] Decrypt(
        DnsCryptEsVersion esVersion,
        byte[] cipherText,
        byte[] nonce,
        byte[] senderPublicKey,
        byte[] recipientSecretKey
    ) =>
        esVersion switch
        {
            DnsCryptEsVersion.XSalsa20Poly1305 => PublicKeyBox.Open(
                cipherText,
                nonce,
                recipientSecretKey,
                senderPublicKey
            ),
            DnsCryptEsVersion.XChaCha20Poly1305 => DecryptXChaCha20Poly1305(
                cipherText,
                nonce,
                senderPublicKey,
                recipientSecretKey
            ),
            _ => throw new NotSupportedException(
                $"DNSCrypt encryption system {esVersion} is not supported."
            ),
        };

    /// <summary>
    ///     This method encrypts a message using <see cref="NativeLibsodium.crypto_box_curve25519xchacha20poly1305_easy" />.
    /// </summary>
    private static byte[] EncryptXChaCha20Poly1305(
        byte[] message,
        byte[] nonce,
        byte[] recipientPublicKey,
        byte[] senderSecretKey
    )
    {
        byte[] cipherText = new byte[message.Length + NativeLibsodium.MacBytes];

        if (
            NativeLibsodium.crypto_box_curve25519xchacha20poly1305_easy(
                cipherText,
                message,
                (ulong)message.Length,
                nonce,
                recipientPublicKey,
                senderSecretKey
            ) != 0
        )
            throw new CryptographicException("libsodium failed to encrypt a message.");

        return cipherText;
    }

    /// <summary>
    ///     This method decrypts a message using <see cref="NativeLibsodium.crypto_box_curve25519xchacha20poly1305_open_easy" />.
    /// </summary>
    private static byte[] DecryptXChaCha20Poly1305(
        byte[] cipherText,
        byte[] nonce,
        byte[] senderPublicKey,
        byte[] recipientSecretKey
    )
    {
        if (cipherText.Length < NativeLibsodium.MacBytes)
            throw new CryptographicException(
                "The ciphertext is too short to contain a valid authentication tag."
            );

        byte[] message = new byte[cipherText.Length - NativeLibsodium.MacBytes];

        if (
            NativeLibsodium.crypto_box_curve25519xchacha20poly1305_open_easy(
                message,
                cipherText,
                (ulong)cipherText.Length,
                nonce,
                senderPublicKey,
                recipientSecretKey
            ) != 0
        )
            throw new CryptographicException(
                "Message authentication failed -- the ciphertext was tampered with, corrupted, or forged."
            );

        return message;
    }
}
