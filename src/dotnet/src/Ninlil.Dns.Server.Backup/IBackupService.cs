using Ninlil.Dns.Server.Backup.Model;

// Define our namespace.
namespace Ninlil.Dns.Server.Backup;

/// <summary>
///     This interface exports and imports a full-system backup archive -- the entire
///     application-level database, the Data Protection key ring, the shared TLS certificate, and
///     the runtime configuration file, bundled together so the resulting file is safe to keep
///     off-site and, restored onto a brand-new install, leaves every previously-encrypted secret
///     still decryptable. Encryption itself is optional: a passphrase is supplied,
///     <see cref="Crypto.BackupArchiveCodec" /> encrypts the archive with it via AES-256-GCM; a
///     passphrase is omitted, the archive is written out unencrypted, clearly marked as such via
///     its own distinct envelope header so <see cref="RestoreBackupAsync" /> can always tell which
///     kind an uploaded archive actually is.
/// </summary>
public interface IBackupService
{
    /// <summary>
    ///     This method builds a complete backup archive, encrypting it with
    ///     <paramref name="passphrase" /> when one is supplied, or leaving it deliberately
    ///     unencrypted (never a weak or default-derived key) when it is not.
    /// </summary>
    /// <param name="passphrase">
    ///     The passphrase to encrypt the archive with, or null/blank to skip encryption
    ///     entirely. This server never stores a supplied passphrase anywhere -- losing it means
    ///     losing the ability to ever decrypt this specific archive again.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The complete archive file's own bytes, encrypted or not per the request above.</returns>
    Task<byte[]> CreateBackupAsync(string passphrase, CancellationToken stoppingToken = default);

    /// <summary>
    ///     This method validates <paramref name="archiveStream" /> in full before writing
    ///     anything -- decrypting it first if its own envelope header says it is encrypted, or
    ///     reading it directly if that header says it is not -- then wipes and replaces every
    ///     entity this server persists, the Data Protection key ring, the shared TLS certificate,
    ///     and the runtime configuration file with the archive's own contents.
    /// </summary>
    /// <param name="archiveStream">The archive to restore from, encrypted or not.</param>
    /// <param name="passphrase">
    ///     The passphrase the archive was encrypted with, if it is an encrypted archive. Ignored
    ///     for an unencrypted archive; required (and validated as such) for an encrypted one.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>A summary of what was imported.</returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="archiveStream" /> is an encrypted archive and
    ///     <paramref name="passphrase" /> is null or blank. Nothing is written when this is
    ///     thrown.
    /// </exception>
    /// <exception cref="BackupDecryptionFailedException">
    ///     Thrown when an encrypted <paramref name="archiveStream" /> cannot be decrypted with
    ///     <paramref name="passphrase" /> -- either the passphrase is wrong, or the archive was
    ///     corrupted or tampered with. Nothing is written when this is thrown.
    /// </exception>
    /// <exception cref="BackupArchiveInvalidException">
    ///     Thrown when <paramref name="archiveStream" /> does not carry a recognizable envelope
    ///     header at all, or decrypts/unwraps successfully but does not structurally look like a
    ///     backup archive this server produced. Nothing is written when this is thrown.
    /// </exception>
    Task<BackupRestoreSummary> RestoreBackupAsync(
        Stream archiveStream,
        string passphrase,
        CancellationToken stoppingToken = default
    );
}
