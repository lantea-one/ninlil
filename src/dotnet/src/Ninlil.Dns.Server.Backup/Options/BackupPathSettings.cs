// Define our namespace.
namespace Ninlil.Dns.Server.Backup.Options;

/// <summary>
///     This class holds the two file system locations <see cref="SystemBackupService" /> backs up
///     and restores: the Data Protection key ring and the runtime configuration file. They are
///     not configuration: the host already decides both (the key ring directory it passes to
///     <c>PersistKeysToFileSystem</c>, the file the runtime settings controllers write), so a
///     second setting could only disagree with it. The host creates one instance and registers
///     it as a singleton before <c>AddNinlilDnsServerBackup</c>; the class is not bound to
///     configuration and has no <c>NINLIL_*</c> variable. <c>Ninlil.Web</c> sets it in
///     <c>Program.cs</c>.
/// </summary>
public sealed class BackupPathSettings
{
    /// <summary>
    ///     This property contains the directory of the Data Protection key ring files. Secrets
    ///     stored encrypted (TSIG secrets, DNSSEC private keys, cloud provider credentials, webhook
    ///     credentials, the OIDC client secret, TOTP keys) can only be decrypted with these files,
    ///     so a backup without them would restore unreadable ciphertext. The default is the
    ///     <c>data</c> directory under the application base; it must match the directory the host
    ///     passes to <c>PersistKeysToFileSystem</c>.
    /// </summary>
    public string DataProtectionKeyRingDirectory { get; set; } =
        Path.Combine(AppContext.BaseDirectory, "data");

    /// <summary>
    ///     This property contains the runtime configuration file a backup includes and a restore
    ///     overwrites: the file the runtime settings controllers write to. It is empty by default;
    ///     when empty, no configuration file is included.
    /// </summary>
    public string RuntimeConfigurationFilePath { get; set; } = "";
}
