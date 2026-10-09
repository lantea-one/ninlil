using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the range checks of the settings controllers whose fields reach a timer,
///     a socket or a bucket (cluster, forwarding, client subnet, rate limiting, query logging, DNS
///     cookies, event broker, identity, recursive, transport and web listen), over HTTP on the
///     shared in-process host, signed in as the bootstrap administrator.
///     <para>
///         A rejected value must give 400 naming the field, and the runtime settings file
///         (<see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />) must be
///         byte-for-byte unchanged: a value saved into a real deployment's file stops the service
///         on the next start, and the controllers must refuse it before anything is written. An
///         accepted boundary value gives 200, and the test puts the file back afterwards so the
///         rows do not depend on each other. Each rejected row sits one step outside a boundary
///         that an accepted row sits on.
///     </para>
///     <para>
///         <c>49.17:02:47.294</c> is <c>TimerMaximum</c> (<c>uint.MaxValue - 1</c> milliseconds,
///         the limit of <c>Task.Delay</c>, <c>CancelAfter</c> and <c>PeriodicTimer</c>); the
///         <c>.295</c> row is one millisecond past it.
///     </para>
/// </summary>
[Collection(WebTestCollection.Name)]
public class SettingsRangeCheckTests(NinlilDnsWebApplicationFactory factory)
{
    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(
                NinlilDnsWebApplicationFactory.AdminUserName,
                NinlilDnsWebApplicationFactory.AdminPassword
            )
        );
        loginResponse.EnsureSuccessStatusCode();

        LoginResponse login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            login.AccessToken
        );

        return client;
    }

    private static HttpRequestMessage PatchRaw(string url, string rawJson) =>
        new(HttpMethod.Patch, url)
        {
            Content = new StringContent(rawJson, Encoding.UTF8, "application/json"),
        };

    private async Task<string> ReadSettingsFileAsync() =>
        File.Exists(factory.RuntimeConfigurationFilePath)
            ? await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath)
            : string.Empty;

    /// <summary>
    ///     This method sends a patch that must be rejected and checks the 400, the field name in
    ///     the message and that the settings file did not change.
    /// </summary>
    private async Task AssertRejectedAsync(string url, string body, string fieldName)
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string before = await ReadSettingsFileAsync();

        HttpResponseMessage response = await client.SendAsync(PatchRaw(url, body));
        string responseText = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(fieldName, responseText);
        Assert.Equal(before, await ReadSettingsFileAsync());
    }

    /// <summary>
    ///     This method sends a patch that must be accepted, then puts the settings file back as it
    ///     was.
    /// </summary>
    private async Task AssertAcceptedAsync(string url, string body)
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        bool existed = File.Exists(factory.RuntimeConfigurationFilePath);
        string before = await ReadSettingsFileAsync();

        try
        {
            HttpResponseMessage response = await client.SendAsync(PatchRaw(url, body));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            if (existed)
                await File.WriteAllTextAsync(factory.RuntimeConfigurationFilePath, before);
            else if (File.Exists(factory.RuntimeConfigurationFilePath))
                File.Delete(factory.RuntimeConfigurationFilePath);
        }
    }

    private const string Cluster = "/api/cluster-settings";
    private const string Forwarding = "/api/forwarding-settings";
    private const string ClientSubnet = "/api/client-subnet-settings";
    private const string RateLimiting = "/api/rate-limiting-settings";
    private const string QueryLogging = "/api/query-logging-settings";
    private const string DnsCookies = "/api/dns-cookie-settings";
    private const string EventBroker = "/api/event-broker-settings";
    private const string Identity = "/api/identity-settings";
    private const string Recursive = "/api/recursive-settings";
    private const string Plain = "/api/transport-settings/plain";
    private const string DnsOverTls = "/api/transport-settings/dns-over-tls";
    private const string DnsOverHttps = "/api/transport-settings/dns-over-https";
    private const string DnsOverQuic = "/api/transport-settings/dns-over-quic";
    private const string DnsCrypt = "/api/transport-settings/dnscrypt";
    private const string WebListen = "/api/web-listen-settings";

    [Theory]
    [InlineData(Cluster, "{\"heartbeatInterval\":\"00:00:00\"}", "heartbeatInterval")]
    [InlineData(Cluster, "{\"heartbeatInterval\":\"-00:00:01\"}", "heartbeatInterval")]
    [InlineData(Cluster, "{\"heartbeatInterval\":\"00:00:00.999\"}", "heartbeatInterval")]
    [InlineData(Cluster, "{\"heartbeatInterval\":\"1.00:00:01\"}", "heartbeatInterval")]
    [InlineData(Cluster, "{\"heartbeatInterval\":30}", "heartbeatInterval")]
    [InlineData(Cluster, "{\"heartbeatTimeout\":\"00:00:00\"}", "heartbeatTimeout")]
    [InlineData(Cluster, "{\"heartbeatTimeout\":\"-00:00:10\"}", "heartbeatTimeout")]
    [InlineData(Cluster, "{\"heartbeatTimeout\":\"00:05:01\"}", "heartbeatTimeout")]
    [InlineData(Cluster, "{\"addressReconcileInterval\":\"00:00:00\"}", "addressReconcileInterval")]
    [InlineData(
        Cluster,
        "{\"addressReconcileInterval\":\"49.17:02:47.295\"}",
        "addressReconcileInterval"
    )]
    [InlineData(Cluster, "{\"missedHeartbeatThreshold\":0}", "missedHeartbeatThreshold")]
    [InlineData(Cluster, "{\"advertisedPort\":65536}", "advertisedPort")]
    [InlineData(Cluster, "{\"advertisedPort\":\"80\"}", "advertisedPort")]
    [InlineData(Cluster, "{\"enable\":\"true\"}", "enable")]
    public async Task Patch_OnClusterSettingsWithAnInvalidValue_ReturnsBadRequestAndWritesNothing(
        string url,
        string body,
        string fieldName
    ) => await AssertRejectedAsync(url, body, fieldName);

    [Theory]
    [InlineData(Cluster, "{\"heartbeatInterval\":\"00:00:01\"}")]
    [InlineData(Cluster, "{\"heartbeatInterval\":\"1.00:00:00\"}")]
    [InlineData(Cluster, "{\"heartbeatTimeout\":\"00:00:01\"}")]
    [InlineData(Cluster, "{\"heartbeatTimeout\":\"00:05:00\"}")]
    [InlineData(Cluster, "{\"addressReconcileInterval\":\"49.17:02:47.294\"}")]
    [InlineData(Cluster, "{\"advertisedPort\":0}")]
    [InlineData(Cluster, "{\"advertisedPort\":65535}")]
    public async Task Patch_OnClusterSettingsAtABoundary_IsAccepted(string url, string body) =>
        await AssertAcceptedAsync(url, body);

    [Theory]
    [InlineData(Forwarding, "{\"healthCheckInterval\":\"-00:00:05\"}", "healthCheckInterval")]
    [InlineData(Forwarding, "{\"healthCheckInterval\":\"00:00:00\"}", "healthCheckInterval")]
    [InlineData(Forwarding, "{\"healthCheckInterval\":\"00:00:00.999\"}", "healthCheckInterval")]
    [InlineData(Forwarding, "{\"healthCheckInterval\":\"1.00:00:01\"}", "healthCheckInterval")]
    [InlineData(Forwarding, "{\"healthCheckInterval\":true}", "healthCheckInterval")]
    [InlineData(Forwarding, "{\"perForwarderTimeout\":\"00:00:00\"}", "perForwarderTimeout")]
    [InlineData(Forwarding, "{\"perForwarderTimeout\":\"-00:00:03\"}", "perForwarderTimeout")]
    [InlineData(Forwarding, "{\"perForwarderTimeout\":\"00:00:00.099\"}", "perForwarderTimeout")]
    [InlineData(Forwarding, "{\"perForwarderTimeout\":\"00:01:00.001\"}", "perForwarderTimeout")]
    [InlineData(Forwarding, "{\"maxCacheEntries\":0}", "maxCacheEntries")]
    [InlineData(Forwarding, "{\"failoverStrategy\":\"Nope\"}", "failoverStrategy")]
    [InlineData(Forwarding, "{\"enableCache\":\"yes\"}", "enableCache")]
    public async Task Patch_OnForwardingSettingsWithAnInvalidValue_ReturnsBadRequestAndWritesNothing(
        string url,
        string body,
        string fieldName
    ) => await AssertRejectedAsync(url, body, fieldName);

    [Theory]
    [InlineData(Forwarding, "{\"healthCheckInterval\":\"00:00:01\"}")]
    [InlineData(Forwarding, "{\"healthCheckInterval\":\"1.00:00:00\"}")]
    [InlineData(Forwarding, "{\"perForwarderTimeout\":\"00:00:00.100\"}")]
    [InlineData(Forwarding, "{\"perForwarderTimeout\":\"00:01:00\"}")]
    public async Task Patch_OnForwardingSettingsAtABoundary_IsAccepted(string url, string body) =>
        await AssertAcceptedAsync(url, body);

    [Theory]
    [InlineData(ClientSubnet, "{\"sourcePrefixLengthIPv4\":33}", "sourcePrefixLengthIPv4")]
    [InlineData(ClientSubnet, "{\"sourcePrefixLengthIPv4\":300}", "sourcePrefixLengthIPv4")]
    [InlineData(ClientSubnet, "{\"sourcePrefixLengthIPv4\":-1}", "sourcePrefixLengthIPv4")]
    [InlineData(ClientSubnet, "{\"sourcePrefixLengthIPv4\":\"24\"}", "sourcePrefixLengthIPv4")]
    [InlineData(ClientSubnet, "{\"sourcePrefixLengthIPv6\":129}", "sourcePrefixLengthIPv6")]
    [InlineData(ClientSubnet, "{\"sourcePrefixLengthIPv6\":300}", "sourcePrefixLengthIPv6")]
    [InlineData(ClientSubnet, "{\"overrideAddress\":5}", "overrideAddress")]
    [InlineData(ClientSubnet, "{\"enable\":1}", "enable")]
    public async Task Patch_OnClientSubnetSettingsWithAnInvalidValue_ReturnsBadRequestAndWritesNothing(
        string url,
        string body,
        string fieldName
    ) => await AssertRejectedAsync(url, body, fieldName);

    [Theory]
    [InlineData(ClientSubnet, "{\"sourcePrefixLengthIPv4\":0}")]
    [InlineData(ClientSubnet, "{\"sourcePrefixLengthIPv4\":32}")]
    [InlineData(ClientSubnet, "{\"sourcePrefixLengthIPv6\":0}")]
    [InlineData(ClientSubnet, "{\"sourcePrefixLengthIPv6\":128}")]
    public async Task Patch_OnClientSubnetSettingsAtABoundary_IsAccepted(string url, string body) =>
        await AssertAcceptedAsync(url, body);

    [Theory]
    [InlineData(RateLimiting, "{\"subnetPrefixLengthIPv4\":0}", "subnetPrefixLengthIPv4")]
    [InlineData(RateLimiting, "{\"subnetPrefixLengthIPv4\":33}", "subnetPrefixLengthIPv4")]
    [InlineData(RateLimiting, "{\"subnetPrefixLengthIPv6\":0}", "subnetPrefixLengthIPv6")]
    [InlineData(RateLimiting, "{\"subnetPrefixLengthIPv6\":129}", "subnetPrefixLengthIPv6")]
    [InlineData(RateLimiting, "{\"subnetBypassCidrs\":[\"10.0.0.1/8\"]}", "10.0.0.1/8")]
    [InlineData(RateLimiting, "{\"subnetBypassCidrs\":[\"2001:db8::1/32\"]}", "2001:db8::1/32")]
    [InlineData(RateLimiting, "{\"subnetBypassCidrs\":[\"nonsense\"]}", "nonsense")]
    [InlineData(RateLimiting, "{\"subnetBypassCidrs\":[\"10.0.0.0/8\",\"bad\"]}", "bad")]
    [InlineData(RateLimiting, "{\"subnetBypassCidrs\":[5]}", "subnetBypassCidrs")]
    [InlineData(RateLimiting, "{\"subnetBypassCidrs\":\"10.0.0.0/8\"}", "subnetBypassCidrs")]
    [InlineData(RateLimiting, "{\"cleanupInterval\":\"-00:01:00\"}", "cleanupInterval")]
    [InlineData(RateLimiting, "{\"cleanupInterval\":\"00:00:00\"}", "cleanupInterval")]
    [InlineData(RateLimiting, "{\"cleanupInterval\":\"49.17:02:47.295\"}", "cleanupInterval")]
    [InlineData(RateLimiting, "{\"trackedClientExpiry\":\"-00:05:00\"}", "trackedClientExpiry")]
    [InlineData(RateLimiting, "{\"trackedClientExpiry\":\"00:00:00\"}", "trackedClientExpiry")]
    [InlineData(RateLimiting, "{\"subnetSampleWindow\":\"00:00:00\"}", "subnetSampleWindow")]
    [InlineData(RateLimiting, "{\"subnetSampleWindow\":\"49.17:02:47.295\"}", "subnetSampleWindow")]
    [InlineData(RateLimiting, "{\"queriesPerSecond\":0}", "queriesPerSecond")]
    [InlineData(RateLimiting, "{\"queriesPerSecond\":\"20\"}", "queriesPerSecond")]
    [InlineData(RateLimiting, "{\"burstSize\":0}", "burstSize")]
    [InlineData(RateLimiting, "{\"maxTrackedClients\":0}", "maxTrackedClients")]
    [InlineData(RateLimiting, "{\"subnetUdpQueryLimit\":0}", "subnetUdpQueryLimit")]
    [InlineData(RateLimiting, "{\"subnetTcpQueryLimit\":0}", "subnetTcpQueryLimit")]
    [InlineData(RateLimiting, "{\"subnetTruncatePercentage\":100.5}", "subnetTruncatePercentage")]
    [InlineData(RateLimiting, "{\"subnetTruncatePercentage\":-1}", "subnetTruncatePercentage")]
    public async Task Patch_OnRateLimitingSettingsWithAnInvalidValue_ReturnsBadRequestAndWritesNothing(
        string url,
        string body,
        string fieldName
    ) => await AssertRejectedAsync(url, body, fieldName);

    [Theory]
    [InlineData(RateLimiting, "{\"subnetPrefixLengthIPv4\":1}")]
    [InlineData(RateLimiting, "{\"subnetPrefixLengthIPv4\":32}")]
    [InlineData(RateLimiting, "{\"subnetPrefixLengthIPv6\":1}")]
    [InlineData(RateLimiting, "{\"subnetPrefixLengthIPv6\":128}")]
    [InlineData(RateLimiting, "{\"subnetBypassCidrs\":[\"10.0.0.0/8\",\"2001:db8::/32\"]}")]
    [InlineData(RateLimiting, "{\"subnetBypassCidrs\":[]}")]
    [InlineData(RateLimiting, "{\"cleanupInterval\":\"49.17:02:47.294\"}")]
    [InlineData(RateLimiting, "{\"cleanupInterval\":\"00:00:00.001\"}")]
    [InlineData(RateLimiting, "{\"queriesPerSecond\":2.5,\"burstSize\":1}")]
    [InlineData(RateLimiting, "{\"subnetTruncatePercentage\":0}")]
    [InlineData(RateLimiting, "{\"subnetTruncatePercentage\":100}")]
    public async Task Patch_OnRateLimitingSettingsAtABoundary_IsAccepted(string url, string body) =>
        await AssertAcceptedAsync(url, body);

    [Theory]
    [InlineData(QueryLogging, "{\"cleanupInterval\":\"-00:01:00\"}", "cleanupInterval")]
    [InlineData(QueryLogging, "{\"cleanupInterval\":\"00:00:00\"}", "cleanupInterval")]
    [InlineData(QueryLogging, "{\"cleanupInterval\":\"49.17:02:47.295\"}", "cleanupInterval")]
    [InlineData(QueryLogging, "{\"cleanupInterval\":60}", "cleanupInterval")]
    [InlineData(QueryLogging, "{\"retentionPeriod\":\"-1.00:00:00\"}", "retentionPeriod")]
    [InlineData(QueryLogging, "{\"retentionPeriod\":\"36501.00:00:00\"}", "retentionPeriod")]
    [InlineData(QueryLogging, "{\"maxRetainedEntries\":-1}", "maxRetainedEntries")]
    [InlineData(QueryLogging, "{\"maxRetainedEntries\":1.5}", "maxRetainedEntries")]
    [InlineData(QueryLogging, "{\"privacyLevel\":5}", "privacyLevel")]
    [InlineData(QueryLogging, "{\"enable\":null}", "enable")]
    public async Task Patch_OnQueryLoggingSettingsWithAnInvalidValue_ReturnsBadRequestAndWritesNothing(
        string url,
        string body,
        string fieldName
    ) => await AssertRejectedAsync(url, body, fieldName);

    [Theory]
    [InlineData(QueryLogging, "{\"cleanupInterval\":\"49.17:02:47.294\"}")]
    [InlineData(QueryLogging, "{\"cleanupInterval\":\"00:00:00.001\"}")]
    [InlineData(QueryLogging, "{\"retentionPeriod\":\"00:00:00\"}")]
    [InlineData(QueryLogging, "{\"retentionPeriod\":\"36500.00:00:00\"}")]
    [InlineData(QueryLogging, "{\"maxRetainedEntries\":0}")]
    public async Task Patch_OnQueryLoggingSettingsAtABoundary_IsAccepted(string url, string body) =>
        await AssertAcceptedAsync(url, body);

    [Theory]
    [InlineData(DnsCookies, "{\"secretRotationInterval\":\"00:00:59\"}", "secretRotationInterval")]
    [InlineData(DnsCookies, "{\"secretRotationInterval\":\"-00:01:00\"}", "secretRotationInterval")]
    [InlineData(DnsCookies, "{\"secretRotationInterval\":\"00:00:00\"}", "secretRotationInterval")]
    [InlineData(
        DnsCookies,
        "{\"secretRotationInterval\":\"49.17:02:47.295\"}",
        "secretRotationInterval"
    )]
    [InlineData(DnsCookies, "{\"secretRotationInterval\":1}", "secretRotationInterval")]
    [InlineData(DnsCookies, "{\"enable\":\"true\"}", "enable")]
    [InlineData(DnsCookies, "{\"require\":0}", "require")]
    public async Task Patch_OnDnsCookieSettingsWithAnInvalidValue_ReturnsBadRequestAndWritesNothing(
        string url,
        string body,
        string fieldName
    ) => await AssertRejectedAsync(url, body, fieldName);

    [Theory]
    [InlineData(DnsCookies, "{\"secretRotationInterval\":\"00:01:00\"}")]
    [InlineData(DnsCookies, "{\"secretRotationInterval\":\"49.17:02:47.294\"}")]
    public async Task Patch_OnDnsCookieSettingsAtABoundary_IsAccepted(string url, string body) =>
        await AssertAcceptedAsync(url, body);

    [Theory]
    [InlineData(
        EventBroker,
        "{\"rabbitMq\":{\"initialReconnectDelay\":\"00:00:00\"}}",
        "rabbitMq.initialReconnectDelay"
    )]
    [InlineData(
        EventBroker,
        "{\"rabbitMq\":{\"initialReconnectDelay\":\"-00:00:01\"}}",
        "rabbitMq.initialReconnectDelay"
    )]
    [InlineData(
        EventBroker,
        "{\"rabbitMq\":{\"maxReconnectDelay\":\"49.17:02:47.295\"}}",
        "rabbitMq.maxReconnectDelay"
    )]
    [InlineData(
        EventBroker,
        "{\"rabbitMq\":{\"initialReconnectDelay\":\"00:01:00\",\"maxReconnectDelay\":\"00:00:10\"}}",
        "rabbitMq.maxReconnectDelay"
    )]
    [InlineData(EventBroker, "{\"rabbitMq\":{\"port\":0}}", "rabbitMq.port")]
    [InlineData(EventBroker, "{\"rabbitMq\":{\"port\":65536}}", "rabbitMq.port")]
    [InlineData(EventBroker, "{\"rabbitMq\":{\"hostName\":5}}", "rabbitMq.hostName")]
    [InlineData(EventBroker, "{\"rabbitMq\":5}", "rabbitMq")]
    [InlineData(
        EventBroker,
        "{\"activeMq\":{\"publishTimeout\":\"00:00:00\"}}",
        "activeMq.publishTimeout"
    )]
    [InlineData(
        EventBroker,
        "{\"activeMq\":{\"publishTimeout\":\"-00:00:05\"}}",
        "activeMq.publishTimeout"
    )]
    [InlineData(
        EventBroker,
        "{\"activeMq\":{\"initialReconnectDelay\":\"00:00:00\"}}",
        "activeMq.initialReconnectDelay"
    )]
    [InlineData(EventBroker, "{\"activeMq\":{\"port\":\"61613\"}}", "activeMq.port")]
    [InlineData(EventBroker, "{\"transport\":\"Bogus\"}", "transport")]
    public async Task Patch_OnEventBrokerSettingsWithAnInvalidValue_ReturnsBadRequestAndWritesNothing(
        string url,
        string body,
        string fieldName
    ) => await AssertRejectedAsync(url, body, fieldName);

    [Theory]
    [InlineData(EventBroker, "{\"rabbitMq\":{\"initialReconnectDelay\":\"00:00:00.001\"}}")]
    [InlineData(EventBroker, "{\"rabbitMq\":{\"maxReconnectDelay\":\"49.17:02:47.294\"}}")]
    [InlineData(
        EventBroker,
        "{\"rabbitMq\":{\"initialReconnectDelay\":\"00:00:10\",\"maxReconnectDelay\":\"00:00:10\"}}"
    )]
    [InlineData(EventBroker, "{\"rabbitMq\":{\"port\":1}}")]
    [InlineData(EventBroker, "{\"rabbitMq\":{\"port\":65535}}")]
    [InlineData(EventBroker, "{\"activeMq\":{\"publishTimeout\":\"00:00:00.001\"}}")]
    public async Task Patch_OnEventBrokerSettingsAtABoundary_IsAccepted(string url, string body) =>
        await AssertAcceptedAsync(url, body);

    [Theory]
    [InlineData(Identity, "{\"defaultLockoutTimeSpan\":\"-00:00:01\"}", "defaultLockoutTimeSpan")]
    [InlineData(
        Identity,
        "{\"defaultLockoutTimeSpan\":\"36501.00:00:00\"}",
        "defaultLockoutTimeSpan"
    )]
    [InlineData(Identity, "{\"defaultLockoutTimeSpan\":900}", "defaultLockoutTimeSpan")]
    [InlineData(Identity, "{\"maxFailedAccessAttempts\":0}", "maxFailedAccessAttempts")]
    [InlineData(Identity, "{\"requiredLength\":0}", "requiredLength")]
    [InlineData(Identity, "{\"requireDigit\":\"yes\"}", "requireDigit")]
    [InlineData(Identity, "{\"allowedForNewUsers\":1}", "allowedForNewUsers")]
    public async Task Patch_OnIdentitySettingsWithAnInvalidValue_ReturnsBadRequestAndWritesNothing(
        string url,
        string body,
        string fieldName
    ) => await AssertRejectedAsync(url, body, fieldName);

    [Theory]
    [InlineData(Identity, "{\"defaultLockoutTimeSpan\":\"00:00:00\"}")]
    [InlineData(Identity, "{\"defaultLockoutTimeSpan\":\"36500.00:00:00\"}")]
    [InlineData(Identity, "{\"maxFailedAccessAttempts\":1}")]
    public async Task Patch_OnIdentitySettingsAtABoundary_IsAccepted(string url, string body) =>
        await AssertAcceptedAsync(url, body);

    [Theory]
    [InlineData(Recursive, "{\"upstreamQueryTimeout\":\"00:00:00\"}", "upstreamQueryTimeout")]
    [InlineData(Recursive, "{\"upstreamQueryTimeout\":\"-00:00:03\"}", "upstreamQueryTimeout")]
    [InlineData(Recursive, "{\"upstreamQueryTimeout\":\"00:00:00.099\"}", "upstreamQueryTimeout")]
    [InlineData(Recursive, "{\"upstreamQueryTimeout\":\"00:01:00.001\"}", "upstreamQueryTimeout")]
    [InlineData(Recursive, "{\"overallQueryTimeout\":\"00:00:00\"}", "overallQueryTimeout")]
    [InlineData(Recursive, "{\"overallQueryTimeout\":\"00:05:01\"}", "overallQueryTimeout")]
    [InlineData(
        Recursive,
        "{\"upstreamQueryTimeout\":\"00:00:30\",\"overallQueryTimeout\":\"00:00:10\"}",
        "overallQueryTimeout"
    )]
    [InlineData(
        Recursive,
        "{\"trustAnchorRefreshInterval\":\"00:00:00\"}",
        "trustAnchorRefreshInterval"
    )]
    [InlineData(
        Recursive,
        "{\"trustAnchorRefreshInterval\":\"49.17:02:47.295\"}",
        "trustAnchorRefreshInterval"
    )]
    [InlineData(
        Recursive,
        "{\"trustAnchorHoldDownDuration\":\"-1.00:00:00\"}",
        "trustAnchorHoldDownDuration"
    )]
    [InlineData(
        Recursive,
        "{\"trustAnchorRemoveHoldDownDuration\":\"-1.00:00:00\"}",
        "trustAnchorRemoveHoldDownDuration"
    )]
    [InlineData(Recursive, "{\"maxStaleServeAge\":\"-00:00:01\"}", "maxStaleServeAge")]
    [InlineData(Recursive, "{\"maxStaleServeAge\":\"36501.00:00:00\"}", "maxStaleServeAge")]
    [InlineData(Recursive, "{\"prefetchWindow\":\"00:00:00\"}", "prefetchWindow")]
    [InlineData(
        Recursive,
        "{\"prefetchRecentAccessWindow\":\"-00:05:00\"}",
        "prefetchRecentAccessWindow"
    )]
    [InlineData(Recursive, "{\"maxReferralDepth\":0}", "maxReferralDepth")]
    [InlineData(Recursive, "{\"maxCnameChainLength\":0}", "maxCnameChainLength")]
    [InlineData(Recursive, "{\"maxUpstreamQueries\":0}", "maxUpstreamQueries")]
    [InlineData(Recursive, "{\"maxCacheEntries\":0}", "maxCacheEntries")]
    [InlineData(Recursive, "{\"enableRecursion\":\"true\"}", "enableRecursion")]
    [InlineData(
        Recursive,
        "{\"rootHintServersOverride\":[{\"name\":\"a\",\"addresses\":[5]}]}",
        "IP address"
    )]
    [InlineData(Recursive, "{\"trustAnchorsOverride\":[{\"keyTag\":\"x\"}]}", "keyTag")]
    [InlineData(
        Recursive,
        "{\"trustAnchorsOverride\":[{\"keyTag\":1,\"algorithm\":5}]}",
        "algorithm"
    )]
    public async Task Patch_OnRecursiveSettingsWithAnInvalidValue_ReturnsBadRequestAndWritesNothing(
        string url,
        string body,
        string fieldName
    ) => await AssertRejectedAsync(url, body, fieldName);

    [Theory]
    [InlineData(Recursive, "{\"upstreamQueryTimeout\":\"00:00:00.100\"}")]
    [InlineData(
        Recursive,
        "{\"upstreamQueryTimeout\":\"00:01:00\",\"overallQueryTimeout\":\"00:05:00\"}"
    )]
    [InlineData(
        Recursive,
        "{\"upstreamQueryTimeout\":\"00:00:00.100\",\"overallQueryTimeout\":\"00:00:01\"}"
    )]
    [InlineData(Recursive, "{\"trustAnchorRefreshInterval\":\"49.17:02:47.294\"}")]
    [InlineData(Recursive, "{\"trustAnchorHoldDownDuration\":\"00:00:00\"}")]
    [InlineData(Recursive, "{\"maxStaleServeAge\":\"36500.00:00:00\"}")]
    [InlineData(Recursive, "{\"prefetchWindow\":\"00:00:00.001\"}")]
    [InlineData(Recursive, "{\"maxReferralDepth\":1}")]
    public async Task Patch_OnRecursiveSettingsAtABoundary_IsAccepted(string url, string body) =>
        await AssertAcceptedAsync(url, body);

    [Theory]
    [InlineData(Plain, "{\"tcpIdleTimeout\":\"00:00:00\"}", "tcpIdleTimeout")]
    [InlineData(Plain, "{\"tcpIdleTimeout\":\"-00:00:01\"}", "tcpIdleTimeout")]
    [InlineData(Plain, "{\"tcpIdleTimeout\":\"1.00:00:01\"}", "tcpIdleTimeout")]
    [InlineData(Plain, "{\"tcpIdleTimeout\":30}", "tcpIdleTimeout")]
    [InlineData(Plain, "{\"port\":65536}", "port")]
    [InlineData(Plain, "{\"port\":-1}", "port")]
    [InlineData(Plain, "{\"maxConcurrentTcpConnections\":0}", "maxConcurrentTcpConnections")]
    [InlineData(DnsOverTls, "{\"idleTimeout\":\"00:00:00\"}", "idleTimeout")]
    [InlineData(DnsOverTls, "{\"idleTimeout\":\"1.00:00:01\"}", "idleTimeout")]
    [InlineData(DnsOverTls, "{\"maxConcurrentConnections\":0}", "maxConcurrentConnections")]
    [InlineData(
        DnsOverTls,
        "{\"certificate\":{\"certificateReloadPollInterval\":\"00:00:00\"}}",
        "certificate.certificateReloadPollInterval"
    )]
    [InlineData(
        DnsOverTls,
        "{\"certificate\":{\"certificateReloadPollInterval\":\"49.17:02:47.295\"}}",
        "certificate.certificateReloadPollInterval"
    )]
    [InlineData(DnsOverTls, "{\"certificate\":{\"pfxPath\":5}}", "certificate.pfxPath")]
    [InlineData(DnsOverTls, "{\"certificate\":5}", "certificate")]
    [InlineData(DnsOverTls, "{\"certificate\":{\"allowedProtocols\":[5]}}", "TLS protocol")]
    [InlineData(DnsOverTls, "{\"enable\":\"true\"}", "enable")]
    [InlineData(DnsOverHttps, "{\"maxMessageSizeBytes\":0}", "maxMessageSizeBytes")]
    [InlineData(DnsOverHttps, "{\"maxMessageSizeBytes\":65536}", "maxMessageSizeBytes")]
    [InlineData(DnsOverHttps, "{\"path\":5}", "path")]
    [InlineData(DnsOverHttps, "{\"path\":null}", "path")]
    [InlineData(DnsOverQuic, "{\"idleTimeout\":\"-00:00:30\"}", "idleTimeout")]
    [InlineData(
        DnsOverQuic,
        "{\"maxConcurrentBidirectionalStreams\":0}",
        "maxConcurrentBidirectionalStreams"
    )]
    [InlineData(DnsOverQuic, "{\"maxConcurrentConnections\":0}", "maxConcurrentConnections")]
    [InlineData(
        DnsOverQuic,
        "{\"certificate\":{\"certificateReloadPollInterval\":\"-00:05:00\"}}",
        "certificate.certificateReloadPollInterval"
    )]
    [InlineData(DnsCrypt, "{\"tcpIdleTimeout\":\"00:00:00\"}", "tcpIdleTimeout")]
    [InlineData(
        DnsCrypt,
        "{\"certificateRotationInterval\":\"00:00:00\"}",
        "certificateRotationInterval"
    )]
    [InlineData(
        DnsCrypt,
        "{\"certificateRotationInterval\":\"49.17:02:47.295\"}",
        "certificateRotationInterval"
    )]
    [InlineData(
        DnsCrypt,
        "{\"certificateValidityDuration\":\"-1.00:00:00\"}",
        "certificateValidityDuration"
    )]
    [InlineData(
        DnsCrypt,
        "{\"certificateValidityDuration\":\"49.17:02:47.295\"}",
        "certificateValidityDuration"
    )]
    [InlineData(
        DnsCrypt,
        "{\"certificateRotationInterval\":\"2.00:00:00\",\"certificateValidityDuration\":\"1.00:00:00\"}",
        "certificateValidityDuration"
    )]
    [InlineData(
        DnsCrypt,
        "{\"certificateRotationInterval\":\"1.00:00:00\",\"certificateValidityDuration\":\"1.00:00:00\"}",
        "certificateValidityDuration"
    )]
    [InlineData(
        DnsCrypt,
        "{\"certificateClockSkewTolerance\":\"-00:00:01\"}",
        "certificateClockSkewTolerance"
    )]
    [InlineData(
        DnsCrypt,
        "{\"certificateClockSkewTolerance\":\"49.17:02:47.295\"}",
        "certificateClockSkewTolerance"
    )]
    [InlineData(DnsCrypt, "{\"enabledEsVersions\":[5]}", "cipher")]
    [InlineData(DnsCrypt, "{\"providerName\":5}", "providerName")]
    [InlineData(DnsCrypt, "{\"maxConcurrentTcpConnections\":0}", "maxConcurrentTcpConnections")]
    public async Task Patch_OnTransportSettingsWithAnInvalidValue_ReturnsBadRequestAndWritesNothing(
        string url,
        string body,
        string fieldName
    ) => await AssertRejectedAsync(url, body, fieldName);

    [Theory]
    [InlineData(Plain, "{\"tcpIdleTimeout\":\"00:00:01\"}")]
    [InlineData(Plain, "{\"tcpIdleTimeout\":\"1.00:00:00\"}")]
    [InlineData(Plain, "{\"port\":0}")]
    [InlineData(Plain, "{\"port\":65535}")]
    [InlineData(DnsOverTls, "{\"idleTimeout\":\"00:00:01\"}")]
    [InlineData(
        DnsOverTls,
        "{\"certificate\":{\"certificateReloadPollInterval\":\"49.17:02:47.294\"}}"
    )]
    [InlineData(DnsOverHttps, "{\"maxMessageSizeBytes\":1}")]
    [InlineData(DnsOverHttps, "{\"maxMessageSizeBytes\":65535}")]
    [InlineData(DnsOverQuic, "{\"maxConcurrentBidirectionalStreams\":1}")]
    [InlineData(DnsCrypt, "{\"certificateClockSkewTolerance\":\"00:00:00\"}")]
    [InlineData(DnsCrypt, "{\"certificateClockSkewTolerance\":\"49.17:02:47.294\"}")]
    [InlineData(
        DnsCrypt,
        "{\"certificateRotationInterval\":\"1.00:00:00\",\"certificateValidityDuration\":\"1.00:00:00.001\"}"
    )]
    public async Task Patch_OnTransportSettingsAtABoundary_IsAccepted(string url, string body) =>
        await AssertAcceptedAsync(url, body);

    [Theory]
    [InlineData(WebListen, "{\"httpPort\":0}", "httpPort")]
    [InlineData(WebListen, "{\"httpPort\":65536}", "httpPort")]
    [InlineData(WebListen, "{\"httpsPort\":\"443\"}", "httpsPort")]
    [InlineData(
        WebListen,
        "{\"certificate\":{\"certificateReloadPollInterval\":\"00:00:00\"}}",
        "certificate.certificateReloadPollInterval"
    )]
    [InlineData(
        WebListen,
        "{\"certificate\":{\"certificateReloadPollInterval\":\"-00:05:00\"}}",
        "certificate.certificateReloadPollInterval"
    )]
    [InlineData(
        WebListen,
        "{\"certificate\":{\"certificateReloadPollInterval\":\"49.17:02:47.295\"}}",
        "certificate.certificateReloadPollInterval"
    )]
    [InlineData(WebListen, "{\"certificate\":{\"pfxPassword\":5}}", "certificate.pfxPassword")]
    [InlineData(WebListen, "{\"certificate\":[]}", "certificate")]
    [InlineData(
        WebListen,
        "{\"certificate\":{\"selfSignedSubjectAlternativeNames\":[5]}}",
        "certificate.selfSignedSubjectAlternativeNames"
    )]
    public async Task Patch_OnWebListenSettingsWithAnInvalidValue_ReturnsBadRequestAndWritesNothing(
        string url,
        string body,
        string fieldName
    ) => await AssertRejectedAsync(url, body, fieldName);

    [Theory]
    [InlineData(WebListen, "{\"httpPort\":1}")]
    [InlineData(WebListen, "{\"httpsPort\":65535}")]
    [InlineData(
        WebListen,
        "{\"certificate\":{\"certificateReloadPollInterval\":\"49.17:02:47.294\"}}"
    )]
    public async Task Patch_OnWebListenSettingsAtABoundary_IsAccepted(string url, string body) =>
        await AssertAcceptedAsync(url, body);
}
