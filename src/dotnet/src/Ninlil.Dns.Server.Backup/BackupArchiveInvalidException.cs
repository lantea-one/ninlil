// Define our namespace.
namespace Ninlil.Dns.Server.Backup;

/// <summary>
///     This exception is thrown when a backup archive decrypts successfully -- proving the
///     passphrase was correct and the ciphertext was not tampered with -- but the plaintext
///     underneath it does not actually look like a backup archive this server produced: the
///     inner container is not a well-formed ZIP file, a required entry is missing, or the
///     manifest's own <see cref="Model.BackupManifest.FormatVersion" /> does not match a version
///     this server knows how to import. Thrown before any real data is touched, so a caller
///     seeing this is guaranteed nothing was written.
/// </summary>
/// <param name="message">A description of the failure, safe to return to the caller as-is.</param>
public sealed class BackupArchiveInvalidException(string message) : Exception(message);
