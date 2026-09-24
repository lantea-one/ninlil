// Define our namespace.
namespace Ninlil.Dns.Server.Backup.Options;

/// <summary>
///     This class configures where a full-system backup finds the two filesystem pieces it
///     bundles alongside the application-level database export: the Data Protection key ring and
///     the minimum passphrase strength an operator must supply before an archive is created or
///     read back. The shared TLS certificate and the runtime configuration file are located
///     through their own already-existing option classes instead, since duplicating either path
///     here would give this project two independent sources of truth for the same setting.
/// </summary>
public sealed class BackupOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Backup";

    /// <summary>
    ///     This property contains the directory holding the Data Protection key ring files this
    ///     process persists its own encryption keys into. Every already-encrypted secret this
    ///     server stores (TSIG shared secrets, DNSSEC private keys, cloud provider credentials,
    ///     webhook auth credentials, TOTP authenticator keys and recovery codes) is only ever
    ///     decryptable again with these exact files back in place, so a backup that omitted them
    ///     would restore a database full of ciphertext nothing could ever read again. Defaults to
    ///     the same directory <c>Program.cs</c> already points <c>AddDataProtection</c>'s own
    ///     <c>PersistKeysToFileSystem</c> call at.
    /// </summary>
    public string DataProtectionKeyRingDirectory { get; set; } =
        Path.Combine(AppContext.BaseDirectory, "data");

    /// <summary>
    ///     This property contains the fewest characters a backup passphrase must contain. A short
    ///     passphrase would make the archive's own AES-256-GCM encryption trivially brute-forceable
    ///     regardless of how strong the cipher itself is, since every already-encrypted secret
    ///     bundled inside the archive is only ever as safe as this one passphrase.
    /// </summary>
    public int MinimumPassphraseLength { get; set; } = 12;

    /// <summary>
    ///     This property contains the runtime configuration file a backup bundles and, on
    ///     restore, overwrites -- the same file every other runtime settings controller
    ///     (<c>PresetsController</c>, <c>EventBrokerSettingsController</c>, and the rest) already
    ///     merges a write into. Left unset by default and populated once, at composition-root
    ///     time, from that same setting's own already-resolved value -- this class does not
    ///     duplicate that default-resolution logic itself, since doing so would give this project
    ///     two independent sources of truth for the same path.
    /// </summary>
    public string RuntimeConfigurationFilePath { get; set; } = "";
}
