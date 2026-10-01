// Define our namespace.
namespace Ninlil.Dns.Server.Backup;

/// <summary>
///     This exception is thrown when an encrypted backup archive cannot be decrypted. AES-256-GCM
///     is authenticated, so one failure covers two causes that cannot be told apart: a wrong
///     passphrase, or an archive changed after it was created (corrupted or tampered with). It
///     is also thrown for an archive too short to hold the encrypted header. Nothing is read or
///     written in either case.
/// </summary>
/// <param name="message">A description of the failure, safe to return to the caller as-is.</param>
public sealed class BackupDecryptionFailedException(string message) : Exception(message);
