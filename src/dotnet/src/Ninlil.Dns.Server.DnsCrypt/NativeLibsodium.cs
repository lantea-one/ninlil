using System.Runtime.InteropServices;

// Define our namespace.
namespace Ninlil.Dns.Server.DnsCrypt;

/// <summary>
///     This class declares the handful of real libsodium C functions
///     <see cref="DnsCryptCryptoBox" /> needs that <c>Sodium.Core</c>'s own C# wrapper does not
///     happen to expose: the <c>crypto_box_curve25519xchacha20poly1305_*</c> family -- DNSCrypt
///     es-version <c>0x0002</c>'s exact construction (X25519 key exchange, HChaCha20 subkey
///     derivation, and XChaCha20-Poly1305 in the NaCl secretbox-style combined-mode layout the
///     DNSCrypt spec's own "Box-XChaChaPoly" formula requires -- not the same wire layout as the
///     IETF AEAD_CHACHA20_POLY1305 construction <c>Sodium.Core</c>'s own
///     <c>SecretAeadXChaCha20Poly1305</c> class wraps).
///     <para>
///         These are thin FFI declarations targeting the exact same native
///         <c>libsodium</c> binary <c>Sodium.Core</c> already depends on and bundles (via its
///         own transitive <c>libsodium</c> NuGet package, which ships RID-specific native
///         binaries for every platform this project targets) -- confirmed present in that
///         binary by direct symbol inspection before writing this. This is not new,
///         unaudited crypto math; it is the same kind of declaration <c>Sodium.Core</c>'s own
///         wrapper classes use internally, just for a few more of the functions the real
///         library already exports.
///     </para>
/// </summary>
internal static partial class NativeLibsodium
{
    /// <summary>
    ///     This constant contains a resolver's own X25519 public key length, in bytes.
    /// </summary>
    public const int PublicKeyBytes = 32;

    /// <summary>
    ///     This constant contains a resolver's own X25519 secret key length, in bytes.
    /// </summary>
    public const int SecretKeyBytes = 32;

    /// <summary>
    ///     This constant contains this construction's own nonce length, in bytes.
    /// </summary>
    public const int NonceBytes = 24;

    /// <summary>
    ///     This constant contains this construction's own authentication tag length, in bytes
    ///     -- prepended to the ciphertext, per both libsodium's "easy" box convention and the
    ///     DNSCrypt wire format itself.
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
