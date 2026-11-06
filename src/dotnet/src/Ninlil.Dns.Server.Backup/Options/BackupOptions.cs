// Define our namespace.
namespace Ninlil.Dns.Server.Backup.Options;

/// <summary>
///     This class binds to the <c>DnsServer:Backup</c> section and configures
///     <see cref="SystemBackupService" />: the minimum passphrase length for a new encrypted
///     archive and the restore size limit. The key ring directory and the runtime configuration
///     file come from <see cref="BackupPathSettings" />, and the TLS certificate path from
///     <c>TlsCertificateOptions</c>, so each has one source.
/// </summary>
public sealed class BackupOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Backup";

    /// <summary>
    ///     This property contains the minimum length of a passphrase for a new encrypted backup.
    ///     A short passphrase could be brute-forced whatever the cipher, and everything in the
    ///     archive, the key ring included, is only as safe as the passphrase. It is not checked on
    ///     restore.
    /// </summary>
    public int MinimumPassphraseLength { get; set; } = 12;

    /// <summary>
    ///     This property contains the most decompressed bytes a restore reads from one archive,
    ///     summed over all entries: 4 GiB by default. The ZIP's own size fields are not trusted, so
    ///     the count is made while the entries are read, which also stops a zip bomb. It exists as
    ///     an option so a test can lower it; there is no reason to change it in production.
    /// </summary>
    public long MaximumRestoredPayloadBytes { get; set; } = 4L * 1024 * 1024 * 1024;
}
