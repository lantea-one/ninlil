using Ninlil.Dns.Protocol.DnsOverTls.ProxyProtocol;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.Core.Cookies;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Presets;
using Ninlil.Dns.Server.RateLimiting;
using Ninlil.Dns.Server.Rewrites;
using Ninlil.Web.ClusterSync;
using Ninlil.Web.Options;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class pins the bytes live settings cluster sync puts on the wire. It records a write
///     for each of the five synced sections through the real
///     <see cref="ClusterConfigSyncSettingsRecorder" /> against in-memory fakes, and compares the
///     published <c>SettingsJson</c> with a literal string, so any change to how the recorder
///     serializes a value (key order, number form, list form, null) fails here. The settings
///     backfill builds its payload through the same serializer, and these tests are what keep the
///     two the same.
/// </summary>
public class ClusterConfigSyncSettingsRecorderWireTests
{
    [Fact]
    public async Task Record_ForCookies_PublishesTheExactPayload()
    {
        string payload = await RecordAsync(
            DnsCookieOptions.SectionName,
            new PresetConfigurationSetting(DnsCookieOptions.SectionName, "Enable", true),
            new PresetConfigurationSetting(DnsCookieOptions.SectionName, "Require", false),
            new PresetConfigurationSetting(
                DnsCookieOptions.SectionName,
                "SecretRotationInterval",
                "12:00:00"
            )
        );

        Assert.Equal(
            "[{\"SectionPath\":\"DnsServer:Cookies\",\"Key\":\"Enable\",\"Value\":true},"
                + "{\"SectionPath\":\"DnsServer:Cookies\",\"Key\":\"Require\",\"Value\":false},"
                + "{\"SectionPath\":\"DnsServer:Cookies\",\"Key\":\"SecretRotationInterval\","
                + "\"Value\":\"12:00:00\"}]",
            payload
        );
    }

    [Fact]
    public async Task Record_ForRewrites_PublishesTheExactPayload()
    {
        string payload = await RecordAsync(
            DnsRewriteOptions.SectionName,
            new PresetConfigurationSetting(DnsRewriteOptions.SectionName, "Enable", true),
            new PresetConfigurationSetting(DnsRewriteOptions.SectionName, "TimeToLiveSeconds", 900u)
        );

        Assert.Equal(
            "[{\"SectionPath\":\"DnsServer:Rewrites\",\"Key\":\"Enable\",\"Value\":true},"
                + "{\"SectionPath\":\"DnsServer:Rewrites\",\"Key\":\"TimeToLiveSeconds\","
                + "\"Value\":900}]",
            payload
        );
    }

    [Fact]
    public async Task Record_ForRateLimiting_PublishesTheExactPayload()
    {
        string payload = await RecordAsync(
            DnsRateLimitOptions.SectionName,
            new PresetConfigurationSetting(
                DnsRateLimitOptions.SectionName,
                "QueriesPerSecond",
                2.5
            ),
            new PresetConfigurationSetting(DnsRateLimitOptions.SectionName, "BurstSize", 42),
            new PresetConfigurationSetting(
                DnsRateLimitOptions.SectionName,
                "CleanupInterval",
                "00:02:00"
            ),
            new PresetConfigurationSetting(
                DnsRateLimitOptions.SectionName,
                "SubnetBypassCidrs",
                new List<string> { "10.0.0.0/8", "fd00::/8" }
            )
        );

        Assert.Equal(
            "[{\"SectionPath\":\"DnsServer:RateLimiting\",\"Key\":\"QueriesPerSecond\",\"Value\":2.5},"
                + "{\"SectionPath\":\"DnsServer:RateLimiting\",\"Key\":\"BurstSize\",\"Value\":42},"
                + "{\"SectionPath\":\"DnsServer:RateLimiting\",\"Key\":\"CleanupInterval\","
                + "\"Value\":\"00:02:00\"},"
                + "{\"SectionPath\":\"DnsServer:RateLimiting\",\"Key\":\"SubnetBypassCidrs\","
                + "\"Value\":[\"10.0.0.0/8\",\"fd00::/8\"]}]",
            payload
        );
    }

    [Fact]
    public async Task Record_ForReverseProxy_PublishesTheSecretAsEnteredAndTheProxyProtocolPath()
    {
        string payload = await RecordAsync(
            ReverseProxyOptions.SectionName,
            new PresetConfigurationSetting(
                ReverseProxyOptions.SectionName,
                "SharedSecret",
                "s3cret"
            ),
            new PresetConfigurationSetting(
                ReverseProxyOptions.SectionName,
                "KnownProxies",
                new List<string> { "192.0.2.1" }
            ),
            new PresetConfigurationSetting(ProxyProtocolOptions.SectionName, "Enable", true),
            new PresetConfigurationSetting(ProxyProtocolOptions.SectionName, "KnownNetworks", null)
        );

        Assert.Equal(
            "[{\"SectionPath\":\"DnsServer:ReverseProxy\",\"Key\":\"SharedSecret\",\"Value\":\"s3cret\"},"
                + "{\"SectionPath\":\"DnsServer:ReverseProxy\",\"Key\":\"KnownProxies\","
                + "\"Value\":[\"192.0.2.1\"]},"
                + "{\"SectionPath\":\"DnsServer:Protocol:DnsOverTls:ProxyProtocol\",\"Key\":\"Enable\","
                + "\"Value\":true},"
                + "{\"SectionPath\":\"DnsServer:Protocol:DnsOverTls:ProxyProtocol\","
                + "\"Key\":\"KnownNetworks\",\"Value\":null}]",
            payload
        );
    }

    [Fact]
    public async Task Record_ForAClearedSecret_PublishesJsonNull()
    {
        string payload = await RecordAsync(
            ReverseProxyOptions.SectionName,
            new PresetConfigurationSetting(ReverseProxyOptions.SectionName, "SharedSecret", null)
        );

        Assert.Equal(
            "[{\"SectionPath\":\"DnsServer:ReverseProxy\",\"Key\":\"SharedSecret\",\"Value\":null}]",
            payload
        );
    }

    [Fact]
    public async Task Record_ForTheEventBroker_PublishesThePasswordAsEnteredUnderThePlainKey()
    {
        string payload = await RecordAsync(
            DnsServerEventPublisherOptions.SectionName,
            new PresetConfigurationSetting(
                DnsServerEventPublisherOptions.SectionName,
                "Transport",
                "RabbitMq"
            ),
            new PresetConfigurationSetting("DnsServer:Events:RabbitMq", "Port", 5672),
            new PresetConfigurationSetting("DnsServer:Events:RabbitMq", "Password", "pw")
        );

        Assert.Equal(
            "[{\"SectionPath\":\"DnsServer:Events\",\"Key\":\"Transport\",\"Value\":\"RabbitMq\"},"
                + "{\"SectionPath\":\"DnsServer:Events:RabbitMq\",\"Key\":\"Port\",\"Value\":5672},"
                + "{\"SectionPath\":\"DnsServer:Events:RabbitMq\",\"Key\":\"Password\","
                + "\"Value\":\"pw\"}]",
            payload
        );
    }

    /// <summary>
    ///     This method runs the real recorder against in-memory fakes and returns the published
    ///     payload.
    /// </summary>
    private static async Task<string> RecordAsync(
        string sectionName,
        params PresetConfigurationSetting[] settings
    )
    {
        SettingsClusterSyncRoundTripTests.CapturingPublisher publisher = new();
        ClusterConfigSyncSettingsRecorder recorder = new(
            new SettingsClusterSyncRoundTripTests.FakeStateRepository(),
            new SettingsClusterSyncRoundTripTests.FakeUnitOfWork(),
            new SettingsClusterSyncRoundTripTests.FakeClusterOptionsMonitor(
                new ClusterOptions { NodeId = Guid.NewGuid() }
            ),
            publisher
        );

        await recorder.RecordAndPublishAsync(sectionName, settings, CancellationToken.None);

        Assert.Equal(sectionName, publisher.Published.SectionName);
        return publisher.Published.SettingsJson;
    }
}
