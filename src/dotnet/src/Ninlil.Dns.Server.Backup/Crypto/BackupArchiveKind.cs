// Define our namespace.
namespace Ninlil.Dns.Server.Backup.Crypto;

/// <summary>
///     This enum names the two envelope kinds <see cref="BackupArchiveCodec" /> can produce and
///     read back, returned by <see cref="BackupArchiveCodec.PeekKind" /> so a caller can decide
///     whether an uploaded archive needs a passphrase to restore at all.
/// </summary>
public enum BackupArchiveKind
{
    /// <summary>
    ///     The archive carries no encryption at all -- an operator deliberately chose to skip
    ///     supplying a passphrase when this archive was created. Its contents are readable by
    ///     anyone who obtains the file.
    /// </summary>
    Unencrypted,

    /// <summary>
    ///     The archive is encrypted with AES-256-GCM, keyed from a passphrase via PBKDF2-HMAC-SHA256
    ///     -- restoring it requires the exact same passphrase it was created with.
    /// </summary>
    Encrypted,
}
