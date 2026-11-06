using Ninlil.Dns.Protocol.DnsOverTls.ProxyProtocol;
using Ninlil.Dns.Server.Core.Cookies;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Events.ActiveMq;
using Ninlil.Dns.Server.Events.RabbitMq;
using Ninlil.Dns.Server.Presets;
using Ninlil.Dns.Server.RateLimiting;
using Ninlil.Dns.Server.Rewrites;
using Ninlil.Web.Options;

// Define our namespace.
namespace Ninlil.Web.ClusterSync;

/// <summary>
///     This record is one cluster-synced settings section: its name (the envelope's entity key)
///     and every configuration key the section syncs, each with the configuration path it sits
///     under (a section can write under more than one path, as the event broker and the reverse
///     proxy do).
/// </summary>
/// <param name="SectionName">
///     The section name every node uses as the envelope key and as the key of its
///     <c>ClusterSyncedSettingsStateEntity</c> row.
/// </param>
/// <param name="Keys">The configuration paths and key names the section syncs.</param>
public sealed record ClusterSyncedSettingsSection(
    string SectionName,
    IReadOnlyList<(string SectionPath, string Key)> Keys
);

/// <summary>
///     This class is the catalogue of every cluster-synced settings section and key. A live
///     settings envelope carries only the keys one write patched, so nothing else says which keys
///     a section syncs. The catalogue is what lets
///     <see cref="SettingsClusterConfigSyncSource" /> send a whole section to a node that joins
///     later, and what lets <see cref="ClusterConfigSyncSettingsRecorder" /> refuse a key that
///     would sync live but never backfill.
///     <para>
///         The keys listed are the plain keys the settings controllers write. An encrypted
///         sibling of a secret (<c>PasswordEncrypted</c>, <c>SharedSecretEncrypted</c>) is never
///         catalogued: ciphertext opens only with the key ring of the node that made it, so only
///         the plaintext key travels (see <see cref="Configuration.ConfigurationSecretSettings" />).
///     </para>
///     <para>
///         A new synced section or key is added here and nowhere else. Until it is, its
///         controller's first write throws, and the catalogue completeness tests fail.
///     </para>
/// </summary>
public static class ClusterSyncedSettingsCatalog
{
    private const string RabbitMq = RabbitMqEventBrokerOptions.SectionName;

    private const string ActiveMq = ActiveMqEventBrokerOptions.SectionName;

    /// <summary>
    ///     This property contains the synced sections: the event broker, rate limiting, DNS
    ///     cookies, the reverse proxy (with its DNS-over-TLS PROXY protocol path) and rewrites.
    /// </summary>
    public static IReadOnlyList<ClusterSyncedSettingsSection> Sections { get; } =
    [
        new(
            DnsServerEventPublisherOptions.SectionName,
            [
                (
                    DnsServerEventPublisherOptions.SectionName,
                    nameof(DnsServerEventPublisherOptions.Transport)
                ),
                (RabbitMq, nameof(RabbitMqEventBrokerOptions.HostName)),
                (RabbitMq, nameof(RabbitMqEventBrokerOptions.Port)),
                (RabbitMq, nameof(RabbitMqEventBrokerOptions.UserName)),
                (RabbitMq, nameof(RabbitMqEventBrokerOptions.Password)),
                (RabbitMq, nameof(RabbitMqEventBrokerOptions.VirtualHost)),
                (RabbitMq, nameof(RabbitMqEventBrokerOptions.ExchangeName)),
                (RabbitMq, nameof(RabbitMqEventBrokerOptions.InitialReconnectDelay)),
                (RabbitMq, nameof(RabbitMqEventBrokerOptions.MaxReconnectDelay)),
                (ActiveMq, nameof(ActiveMqEventBrokerOptions.HostName)),
                (ActiveMq, nameof(ActiveMqEventBrokerOptions.Port)),
                (ActiveMq, nameof(ActiveMqEventBrokerOptions.UserName)),
                (ActiveMq, nameof(ActiveMqEventBrokerOptions.Password)),
                (ActiveMq, nameof(ActiveMqEventBrokerOptions.TopicName)),
                (ActiveMq, nameof(ActiveMqEventBrokerOptions.InitialReconnectDelay)),
                (ActiveMq, nameof(ActiveMqEventBrokerOptions.MaxReconnectDelay)),
                (ActiveMq, nameof(ActiveMqEventBrokerOptions.PublishTimeout)),
            ]
        ),
        new(
            DnsRateLimitOptions.SectionName,
            [
                (DnsRateLimitOptions.SectionName, nameof(DnsRateLimitOptions.Enable)),
                (DnsRateLimitOptions.SectionName, nameof(DnsRateLimitOptions.QueriesPerSecond)),
                (DnsRateLimitOptions.SectionName, nameof(DnsRateLimitOptions.BurstSize)),
                (DnsRateLimitOptions.SectionName, nameof(DnsRateLimitOptions.TrackedClientExpiry)),
                (DnsRateLimitOptions.SectionName, nameof(DnsRateLimitOptions.CleanupInterval)),
                (DnsRateLimitOptions.SectionName, nameof(DnsRateLimitOptions.MaxTrackedClients)),
                (
                    DnsRateLimitOptions.SectionName,
                    nameof(DnsRateLimitOptions.SubnetPrefixLengthIPv4)
                ),
                (
                    DnsRateLimitOptions.SectionName,
                    nameof(DnsRateLimitOptions.SubnetPrefixLengthIPv6)
                ),
                (DnsRateLimitOptions.SectionName, nameof(DnsRateLimitOptions.SubnetSampleWindow)),
                (DnsRateLimitOptions.SectionName, nameof(DnsRateLimitOptions.SubnetUdpQueryLimit)),
                (DnsRateLimitOptions.SectionName, nameof(DnsRateLimitOptions.SubnetTcpQueryLimit)),
                (
                    DnsRateLimitOptions.SectionName,
                    nameof(DnsRateLimitOptions.SubnetTruncatePercentage)
                ),
                (DnsRateLimitOptions.SectionName, nameof(DnsRateLimitOptions.SubnetBypassCidrs)),
            ]
        ),
        new(
            DnsCookieOptions.SectionName,
            [
                (DnsCookieOptions.SectionName, nameof(DnsCookieOptions.Enable)),
                (DnsCookieOptions.SectionName, nameof(DnsCookieOptions.Require)),
                (DnsCookieOptions.SectionName, nameof(DnsCookieOptions.SecretRotationInterval)),
            ]
        ),
        new(
            ReverseProxyOptions.SectionName,
            [
                (ReverseProxyOptions.SectionName, nameof(ReverseProxyOptions.Enable)),
                (ReverseProxyOptions.SectionName, nameof(ReverseProxyOptions.KnownProxies)),
                (ReverseProxyOptions.SectionName, nameof(ReverseProxyOptions.KnownNetworks)),
                (
                    ReverseProxyOptions.SectionName,
                    nameof(ReverseProxyOptions.SharedSecretHeaderName)
                ),
                (ReverseProxyOptions.SectionName, nameof(ReverseProxyOptions.SharedSecret)),
                (ProxyProtocolOptions.SectionName, nameof(ProxyProtocolOptions.Enable)),
                (ProxyProtocolOptions.SectionName, nameof(ProxyProtocolOptions.KnownProxies)),
                (ProxyProtocolOptions.SectionName, nameof(ProxyProtocolOptions.KnownNetworks)),
            ]
        ),
        new(
            DnsRewriteOptions.SectionName,
            [
                (DnsRewriteOptions.SectionName, nameof(DnsRewriteOptions.Enable)),
                (DnsRewriteOptions.SectionName, nameof(DnsRewriteOptions.TimeToLiveSeconds)),
            ]
        ),
    ];

    /// <summary>
    ///     This method finds a section by name.
    /// </summary>
    /// <param name="sectionName">The section name (the envelope's entity key).</param>
    /// <returns>The section, or <see langword="null" /> when it is not catalogued.</returns>
    public static ClusterSyncedSettingsSection Find(string sectionName) =>
        Sections.FirstOrDefault(section =>
            string.Equals(section.SectionName, sectionName, StringComparison.Ordinal)
        );

    /// <summary>
    ///     This method checks that a section and every setting of a write are catalogued.
    ///     Configuration paths and keys are case-insensitive, so the comparison is too.
    /// </summary>
    /// <param name="sectionName">The section name the write is recorded under.</param>
    /// <param name="settings">The settings the write carries.</param>
    /// <exception cref="InvalidOperationException">
    ///     The section, or one of the settings, is not in the catalogue.
    /// </exception>
    public static void EnsureCatalogued(
        string sectionName,
        IReadOnlyList<PresetConfigurationSetting> settings
    )
    {
        ClusterSyncedSettingsSection section =
            Find(sectionName)
            ?? throw new InvalidOperationException(
                $"The settings section '{sectionName}' is not in ClusterSyncedSettingsCatalog, so a "
                    + "node that joins later would never receive it. Add it to the catalogue."
            );

        foreach (PresetConfigurationSetting setting in settings)
        {
            bool known = section.Keys.Any(key =>
                string.Equals(
                    key.SectionPath,
                    setting.SectionPath,
                    StringComparison.OrdinalIgnoreCase
                ) && string.Equals(key.Key, setting.Key, StringComparison.OrdinalIgnoreCase)
            );

            if (!known)
                throw new InvalidOperationException(
                    $"The setting '{setting.SectionPath}:{setting.Key}' of section '{sectionName}' is "
                        + "not in ClusterSyncedSettingsCatalog, so a node that joins later would never "
                        + "receive it. Add it to the catalogue."
                );
        }
    }
}
