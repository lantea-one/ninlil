// Define our namespace.
namespace Ninlil.Dns.Server.Backup;

/// <summary>
///     This interface lets the host test whether the Data Protection key files in a backup can
///     be read by this server before a restore changes anything. <see cref="SystemBackupService" />
///     calls it once, after the archive is validated and before the database is wiped, when the
///     restore includes files. A key ring that cannot be read here (encrypted with DPAPI on
///     another machine or account, or with a certificate this server is not configured with)
///     would leave every restored secret undecryptable, so the restore is refused. The host
///     (<c>Ninlil.Web</c>) registers the implementation, because only it knows how the key ring
///     is encrypted; without one the check is skipped.
/// </summary>
public interface IKeyRingRestoreCheck
{
    /// <summary>
    ///     This method checks the key files of an archive.
    /// </summary>
    /// <param name="keyFiles">The file name and XML bytes of every key ring entry in the archive.</param>
    /// <returns>
    ///     <see langword="null" /> when every key can be read, otherwise a message that names the
    ///     cause and the setting to change, safe to return to the caller as-is.
    /// </returns>
    string Check(IReadOnlyList<(string FileName, byte[] Bytes)> keyFiles);
}
