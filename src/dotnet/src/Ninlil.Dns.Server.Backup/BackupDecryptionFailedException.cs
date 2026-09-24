// Define our namespace.
namespace Ninlil.Dns.Server.Backup;

/// <summary>
///     This exception is thrown when a backup archive cannot be decrypted. AES-256-GCM is an
///     authenticated cipher, so this single failure mode genuinely covers two different causes a
///     caller cannot be told apart without weakening the cipher itself: the supplied passphrase
///     was wrong, or the archive's own ciphertext was altered (corrupted in transit, or
///     deliberately tampered with) after it was created. Either way, the archive's own contents
///     were never read, and nothing was written to this server.
/// </summary>
/// <param name="message">A description of the failure, safe to return to the caller as-is.</param>
public sealed class BackupDecryptionFailedException(string message) : Exception(message);
