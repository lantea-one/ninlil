// Define our namespace.
namespace Ninlil.Dns.Server.Backup.Options;

/// <summary>
///     This class binds to the <c>DnsServer:Backup</c> section and configures
///     <see cref="SystemBackupService" />: where the Data Protection key ring is, which runtime
///     configuration file to include, and the minimum passphrase length for a new encrypted
///     archive. The TLS certificate path comes from <c>TlsCertificateOptions</c> instead, so
///     there is one source for it.
/// </summary>
public sealed class BackupOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Backup";

    /// <summary>
    ///     This property contains the directory of the Data Protection key ring files. Secrets
    ///     stored encrypted (TSIG secrets, DNSSEC private keys, cloud provider credentials, webhook
    ///     credentials, the OIDC client secret, TOTP keys) can only be decrypted with these files,
    ///     so a backup without them would restore unreadable ciphertext. The default is the
    ///     <c>data</c> directory that <c>Program.cs</c> passes to <c>PersistKeysToFileSystem</c>;
    ///     the two must match.
    /// </summary>
    public string DataProtectionKeyRingDirectory { get; set; } =
        Path.Combine(AppContext.BaseDirectory, "data");

    /// <summary>
    ///     This property contains the minimum length of a passphrase for a new encrypted backup.
    ///     A short passphrase could be brute-forced whatever the cipher, and everything in the
    ///     archive, the key ring included, is only as safe as the passphrase. It is not checked on
    ///     restore.
    /// </summary>
    public int MinimumPassphraseLength { get; set; } = 12;

    /// <summary>
    ///     This property contains the runtime configuration file a backup includes and a restore
    ///     overwrites: the file the runtime settings controllers write to. It is empty by default
    ///     and set by <c>Program.cs</c> from <c>RuntimeConfigurationOptions.ConfigurationFilePath</c>,
    ///     so the path is resolved in one place. When empty, no configuration file is included.
    /// </summary>
    public string RuntimeConfigurationFilePath { get; set; } = "";
}
