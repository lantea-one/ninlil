// Define our namespace.
namespace Ninlil.Dns.Server.Backup;

/// <summary>
///     This exception is thrown when an uploaded file is not a Ninlil backup archive: it has no
///     known envelope header, or it decrypts or unwraps but the content is not a well-formed
///     ZIP, lacks a required entry or a readable manifest, or has a
///     <see cref="Model.BackupManifest.FormatVersion" /> this server cannot import.
///     <see cref="SystemBackupService" /> throws it before anything is written.
/// </summary>
/// <param name="message">A description of the failure, safe to return to the caller as-is.</param>
public sealed class BackupArchiveInvalidException(string message) : Exception(message);
