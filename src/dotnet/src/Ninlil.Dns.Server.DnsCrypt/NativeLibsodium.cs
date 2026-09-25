using System.Runtime.InteropServices;

// Define our namespace.
namespace Ninlil.Dns.Server.DnsCrypt;

/// <summary>
///     This class declares the libsodium C functions <see cref="DnsCryptCryptoBox" /> needs that
///     the <c>Sodium.Core</c> wrapper does not expose: the
///     <c>crypto_box_curve25519xchacha20poly1305_*</c> family. That is the construction of
///     DNSCrypt es-version <c>0x0002</c> ("Box-XChaChaPoly"): X25519 key exchange, HChaCha20
///     subkey derivation, and XChaCha20-Poly1305 in the NaCl box layout. It is not the IETF
///     AEAD construction that <c>SecretAeadXChaCha20Poly1305</c> in <c>Sodium.Core</c> wraps,
///     whose layout differs.
///     <para>
///         The declarations call the same native <c>libsodium</c> library <c>Sodium.Core</c>
///         uses, which its <c>libsodium</c> NuGet dependency ships for each supported runtime.
///         No crypto is implemented here; these are the same kind of declarations
///         <c>Sodium.Core</c> uses internally.
///     </para>
/// </summary>
internal static partial class NativeLibsodium
{
    /// <summary>
    ///     This constant contains the X25519 public key length, in bytes.
    /// </summary>
    public const int PublicKeyBytes = 32;

    /// <summary>
    ///     This constant contains the X25519 secret key length, in bytes.
    /// </summary>
    public const int SecretKeyBytes = 32;

    /// <summary>
    ///     This constant contains the nonce length, in bytes.
    /// </summary>
    public const int NonceBytes = 24;

    /// <summary>
    ///     This constant contains the authentication tag length, in bytes. The tag comes before
    ///     the ciphertext, in both the libsodium "easy" box layout and the DNSCrypt wire format.
    /// </summary>
    public const int MacBytes = 16;

    [LibraryImport("libsodium")]
    internal static partial int crypto_box_curve25519xchacha20poly1305_keypair(
        byte[] publicKey,
        byte[] secretKey
    );

    [LibraryImport("libsodium")]
    internal static partial int crypto_box_curve25519xchacha20poly1305_easy(
        byte[] cipherText,
        byte[] message,
        ulong messageLength,
        byte[] nonce,
        byte[] recipientPublicKey,
        byte[] senderSecretKey
    );

    [LibraryImport("libsodium")]
    internal static partial int crypto_box_curve25519xchacha20poly1305_open_easy(
        byte[] message,
        byte[] cipherText,
        ulong cipherTextLength,
        byte[] nonce,
        byte[] senderPublicKey,
        byte[] recipientSecretKey
    );
}
