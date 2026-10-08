using Ninlil.Dns.Server.Backup.Model;

// Define our namespace.
namespace Ninlil.Dns.Server.Backup;

/// <summary>
///     This interface exports and imports a full-system backup archive: the application
///     database (apart from history tables, see <see cref="Catalog.BackupEntityCatalog" />), the
///     Data Protection key ring, the shared TLS certificate and the runtime configuration file.
///     Including the key ring means secrets encrypted in the database can still be decrypted
///     after a restore onto a new installation. Encryption is optional: with a passphrase,
///     <see cref="Crypto.BackupArchiveCodec" /> encrypts the archive with AES-256-GCM; without
///     one, the archive is written unencrypted with its own envelope header, so
///     <see cref="RestoreBackupAsync" /> can tell the two kinds apart.
/// </summary>
public interface IBackupService
{
    /// <summary>
    ///     This method builds a complete backup archive, encrypting it with
    ///     <paramref name="passphrase" /> when one is supplied, or leaving it unencrypted (not
    ///     encrypted with a weak default key) when it is not.
    /// </summary>
    /// <param name="passphrase">
    ///     The passphrase to encrypt the archive with, or null or empty for no encryption. A
    ///     passphrase of only whitespace is rejected. It must be at least
    ///     <c>BackupOptions.MinimumPassphraseLength</c> characters. The server never stores it, so
    ///     a lost passphrase means the archive cannot be decrypted.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The bytes of the archive file.</returns>
    /// <exception cref="ArgumentException">
    ///     Thrown when <paramref name="passphrase" /> is given but only whitespace or too short.
    /// </exception>
    Task<byte[]> CreateBackupAsync(string passphrase, CancellationToken stoppingToken = default);

    /// <summary>
    ///     This method decrypts or unwraps <paramref name="archiveStream" /> (as its envelope header
    ///     says) and checks that every required entry is present and that the bundled files are
    ///     sound (key ring entries named <c>key-&lt;guid&gt;.xml</c> holding XML, at most one
    ///     certificate and one configuration entry, the configuration a JSON object, no more than
    ///     4 GiB when unpacked). It then wipes and replaces the backed-up database contents and
    ///     replaces the shared TLS certificate and the runtime configuration file. Every entity
    ///     file is parsed before anything is wiped, the wipe and import run in one database
    ///     transaction where the storage engine supports it (a failure rolls it back; MongoDB
    ///     does not yet, and a failure there can leave a partly restored database), and the files
    ///     are staged first and put in place only after that transaction commits. The archive's Data
    ///     Protection keys are added to the key ring; keys already there are kept, so a secret the
    ///     running process protects before the restart stays readable. The running process keeps
    ///     its loaded keys, certificate and settings, so a restart is needed afterwards. Only one
    ///     restore runs at a time.
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
    ///     <paramref name="passphrase" />: the passphrase is wrong, or the archive was corrupted or
    ///     tampered with. Nothing is written when this is thrown.
    /// </exception>
    /// <exception cref="BackupArchiveInvalidException">
    ///     Thrown when <paramref name="archiveStream" /> does not carry a recognizable envelope
    ///     header, or decrypts or unwraps but is not a backup archive this server can import
    ///     (including an unexpected key ring entry, a repeated certificate or configuration entry,
    ///     a configuration that is not JSON, entity data that does not parse, or too much unpacked
    ///     data). Nothing is written when this is thrown.
    /// </exception>
    /// <exception cref="BackupRestoreInProgressException">
    ///     Thrown when another restore is still running. Nothing is changed.
    /// </exception>
    Task<BackupRestoreSummary> RestoreBackupAsync(
        Stream archiveStream,
        string passphrase,
        CancellationToken stoppingToken = default
    );
}
