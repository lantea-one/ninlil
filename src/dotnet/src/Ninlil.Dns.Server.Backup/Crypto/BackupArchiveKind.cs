// Define our namespace.
namespace Ninlil.Dns.Server.Backup.Crypto;

/// <summary>
///     This enum names the two envelope kinds <see cref="BackupArchiveCodec" /> writes and reads.
///     <see cref="BackupArchiveCodec.PeekKind" /> returns it so a caller knows whether an uploaded
///     archive needs a passphrase.
/// </summary>
public enum BackupArchiveKind
{
    /// <summary>
    ///     The archive is not encrypted, because no passphrase was given when it was created.
    ///     Anyone with the file can read it, including the key ring that decrypts its secrets.
    /// </summary>
    Unencrypted,

    /// <summary>
    ///     The archive is encrypted with AES-256-GCM, keyed from a passphrase with
    ///     PBKDF2-HMAC-SHA256. Restoring it needs the same passphrase.
    /// </summary>
    Encrypted,
}
