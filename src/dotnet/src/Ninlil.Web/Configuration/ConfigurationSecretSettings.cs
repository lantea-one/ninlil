using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Ninlil.Dns.Server.Events.ActiveMq;
using Ninlil.Dns.Server.Events.RabbitMq;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Options;

// Define our namespace.
namespace Ninlil.Web.Configuration;

/// <summary>
///     This record describes one secret that lives in a cluster-synced settings section: where
///     its plaintext and encrypted forms sit in the configuration, and which Data Protection
///     purpose protects it.
/// </summary>
/// <param name="SectionPath">The configuration section both keys belong to, for example <c>DnsServer:ReverseProxy</c>.</param>
/// <param name="PlainKey">The key that holds the plaintext (an environment variable, or a configuration file edited by hand).</param>
/// <param name="EncryptedKey">The key that holds the Data Protection payload.</param>
/// <param name="ProtectorPurpose">The purpose string the payload is protected with.</param>
public sealed record ConfigurationSecretSetting(
    string SectionPath,
    string PlainKey,
    string EncryptedKey,
    string ProtectorPurpose
)
{
    /// <summary>
    ///     This property contains the configuration path of the plaintext key, as
    ///     <see cref="Microsoft.Extensions.Configuration.IConfiguration" /> reads it.
    /// </summary>
    public string PlainPath => $"{SectionPath}:{PlainKey}";

    /// <summary>
    ///     This property contains a name for log messages, for example
    ///     <c>DnsServer:ReverseProxy:SharedSecret</c>.
    /// </summary>
    public string DisplayName => PlainPath;
}

/// <summary>
///     This class is the catalog of secrets that are stored encrypted at rest on each node although
///     their settings section is cluster-synced, and the one place that turns a plaintext secret
///     into what is written to a node's configuration file.
///     <para>
///         <b>The threat.</b> The runtime configuration file is copied into backups, support
///         bundles and configuration management, and every cluster node holds a copy. A secret in
///         it in the clear is readable by anyone who sees any of those copies. Every other secret
///         Ninlil stores (the SMTP password, the SSO client secret, TSIG keys) is encrypted with
///         the Data Protection key ring of the node, which stays in the data directory, so the
///         catalog gives the synced secrets the same protection.
///     </para>
///     <para>
///         <b>Why the cluster channel carries plaintext.</b> A Data Protection payload opens only
///         with the key ring of the node that made it, so a payload copied to a peer is
///         unreadable there. A write is therefore sent to peers as entered, over the TLS and TSIG
///         protected cluster channel only (never the message broker), and each node encrypts it
///         with its own key ring through <see cref="ProtectForFile" /> before it writes the file.
///     </para>
/// </summary>
public static class ConfigurationSecretSettings
{
    /// <summary>
    ///     This field contains every catalogued secret: the reverse-proxy shared secret and the
    ///     RabbitMQ and ActiveMQ broker passwords.
    /// </summary>
    private static readonly ConfigurationSecretSetting[] Entries =
    [
        new(
            ReverseProxyOptions.SectionName,
            nameof(ReverseProxyOptions.SharedSecret),
            nameof(ReverseProxyOptions.SharedSecretEncrypted),
            ReverseProxyOptions.SharedSecretProtectorPurpose
        ),
        new(
            RabbitMqEventBrokerOptions.SectionName,
            nameof(RabbitMqEventBrokerOptions.Password),
            nameof(RabbitMqEventBrokerOptions.PasswordEncrypted),
            RabbitMqEventBrokerOptions.PasswordProtectorPurpose
        ),
        new(
            ActiveMqEventBrokerOptions.SectionName,
            nameof(ActiveMqEventBrokerOptions.Password),
            nameof(ActiveMqEventBrokerOptions.PasswordEncrypted),
            ActiveMqEventBrokerOptions.PasswordProtectorPurpose
        ),
    ];

    /// <summary>
    ///     This property contains every catalogued secret.
    /// </summary>
    public static IReadOnlyList<ConfigurationSecretSetting> All => Entries;

    /// <summary>
    ///     This method encrypts a plaintext secret with the key ring of this node.
    /// </summary>
    /// <param name="secret">The catalog entry that names the purpose.</param>
    /// <param name="dataProtectionProvider">Supplies this node's key ring.</param>
    /// <param name="plaintext">The secret to encrypt.</param>
    public static string Protect(
        ConfigurationSecretSetting secret,
        IDataProtectionProvider dataProtectionProvider,
        string plaintext
    ) => dataProtectionProvider.CreateProtector(secret.ProtectorPurpose).Protect(plaintext);

    /// <summary>
    ///     This method decrypts a payload written by <see cref="Protect" />.
    /// </summary>
    /// <param name="secret">The catalog entry that names the purpose.</param>
    /// <param name="dataProtectionProvider">Supplies this node's key ring.</param>
    /// <param name="payload">The Data Protection payload.</param>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    ///     The payload was not protected with this key ring.
    /// </exception>
    public static string Unprotect(
        ConfigurationSecretSetting secret,
        IDataProtectionProvider dataProtectionProvider,
        string payload
    ) => dataProtectionProvider.CreateProtector(secret.ProtectorPurpose).Unprotect(payload);

    /// <summary>
    ///     This method returns the settings to write to this node's configuration file. A
    ///     catalogued plaintext secret becomes its encrypted sibling (protected with this node's
    ///     key ring) plus an empty plaintext key, so the plaintext never reaches the file. A
    ///     <see langword="null" /> or empty secret clears both keys. Other settings pass through
    ///     unchanged.
    ///     <para>
    ///         An incoming value for an encrypted key is dropped. A payload made by another node's
    ///         key ring cannot be read here, and writing it would replace a working secret with an
    ///         unreadable one (the reverse proxy then fails closed and a broker password is empty). Only the plaintext
    ///         travels between nodes.
    ///     </para>
    /// </summary>
    /// <param name="settings">
    ///     The values as the operator entered them, or as a peer sent them. A value from a peer is
    ///     a <see cref="JsonElement" />.
    /// </param>
    /// <param name="dataProtectionProvider">Supplies this node's key ring.</param>
    public static IReadOnlyList<PresetConfigurationSetting> ProtectForFile(
        IReadOnlyList<PresetConfigurationSetting> settings,
        IDataProtectionProvider dataProtectionProvider
    )
    {
        List<PresetConfigurationSetting> result = new(settings.Count);

        foreach (PresetConfigurationSetting setting in settings)
        {
            ConfigurationSecretSetting plainMatch = Find(setting, secret => secret.PlainKey);

            if (plainMatch is not null)
            {
                string plaintext = ReadString(setting.Value);

                result.Add(
                    new PresetConfigurationSetting(
                        plainMatch.SectionPath,
                        plainMatch.EncryptedKey,
                        string.IsNullOrEmpty(plaintext)
                            ? string.Empty
                            : Protect(plainMatch, dataProtectionProvider, plaintext)
                    )
                );
                result.Add(
                    new PresetConfigurationSetting(
                        plainMatch.SectionPath,
                        plainMatch.PlainKey,
                        string.Empty
                    )
                );
                continue;
            }

            if (Find(setting, secret => secret.EncryptedKey) is not null)
                continue;

            result.Add(setting);
        }

        return result;
    }

    /// <summary>
    ///     This method finds the catalog entry whose key (picked by <paramref name="key" />) a
    ///     setting writes. Configuration keys are case-insensitive, so the match is too.
    /// </summary>
    private static ConfigurationSecretSetting Find(
        PresetConfigurationSetting setting,
        Func<ConfigurationSecretSetting, string> key
    ) =>
        Entries.FirstOrDefault(secret =>
            string.Equals(
                secret.SectionPath,
                setting.SectionPath,
                StringComparison.OrdinalIgnoreCase
            ) && string.Equals(key(secret), setting.Key, StringComparison.OrdinalIgnoreCase)
        );

    /// <summary>
    ///     This method reads a setting value as text: a string, a JSON string (from a peer), or
    ///     <see langword="null" /> for a missing or JSON null value. Any other JSON kind is read
    ///     as its raw text, so a secret that a peer sent as a number is still protected rather
    ///     than written in the clear.
    /// </summary>
    private static string ReadString(object value) =>
        value switch
        {
            null => null,
            string text => text,
            JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => null,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            JsonElement element => element.GetRawText(),
            _ => value.ToString(),
        };
}
