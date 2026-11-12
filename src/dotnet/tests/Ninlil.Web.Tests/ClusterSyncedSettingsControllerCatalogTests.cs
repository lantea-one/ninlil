using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ninlil.Web.ClusterSync;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class proves <see cref="ClusterSyncedSettingsCatalog" /> is complete against the live
///     sync code paths. For each synced settings controller it sends a patch that sets every field
///     the endpoint takes, on the shared host. The controller calls the real
///     <see cref="ClusterConfigSyncSettingsRecorder" />, which throws for a key the catalogue
///     lacks, so a 500 here means a controller writes a key nobody catalogued. It then reads the
///     host's runtime file and checks that every key under the section's paths is catalogued or the
///     encrypted sibling of a secret. A field added to a controller must be added to the patch
///     here, and the file check catches the case where it is not.
///     <para>
///         <b>Limit.</b> The controllers take a raw <c>JsonElement</c> patch, so there is no
///         request model to enumerate by reflection. A new field with no entry in the patch body
///         below is not exercised, and nothing here fails for it; the reviewer of a new field
///         adds it to the body and to the catalogue.
///     </para>
/// </summary>
[Collection(WebTestCollection.Name)]
public class ClusterSyncedSettingsControllerCatalogTests(NinlilDnsWebApplicationFactory factory)
{
    /// <summary>
    ///     This method returns the route, the section a controller records and a patch body that
    ///     sets every field, for each of the five synced controllers.
    /// </summary>
    public static TheoryData<string, string, string> Controllers() =>
        new()
        {
            {
                "event-broker",
                "/api/event-broker-settings",
                JsonSerializer.Serialize(
                    new
                    {
                        transport = "InMemory",
                        rabbitMq = new
                        {
                            hostName = "rabbit.example.internal",
                            port = 5672,
                            userName = "u",
                            password = "p",
                            virtualHost = "/",
                            exchangeName = "ex",
                            initialReconnectDelay = "00:00:01",
                            maxReconnectDelay = "00:00:30",
                        },
                        activeMq = new
                        {
                            hostName = "active.example.internal",
                            port = 61616,
                            userName = "u",
                            password = "p",
                            topicName = "topic",
                            initialReconnectDelay = "00:00:01",
                            maxReconnectDelay = "00:00:30",
                            publishTimeout = "00:00:02",
                        },
                    }
                )
            },
            {
                "rate-limiting",
                "/api/rate-limiting-settings",
                JsonSerializer.Serialize(
                    new
                    {
                        enable = false,
                        queriesPerSecond = 5.0,
                        burstSize = 10,
                        trackedClientExpiry = "00:10:00",
                        cleanupInterval = "00:01:00",
                        maxTrackedClients = 1000,
                        subnetPrefixLengthIPv4 = 24,
                        subnetPrefixLengthIPv6 = 56,
                        subnetSampleWindow = "00:00:30",
                        subnetUdpQueryLimit = 500,
                        subnetTcpQueryLimit = 200,
                        subnetTruncatePercentage = 10.0,
                        subnetBypassCidrs = new[] { "10.0.0.0/8" },
                    }
                )
            },
            {
                "dns-cookies",
                "/api/dns-cookie-settings",
                JsonSerializer.Serialize(
                    new
                    {
                        enable = true,
                        require = false,
                        secretRotationInterval = "12:00:00",
                    }
                )
            },
            {
                "reverse-proxy",
                "/api/reverse-proxy-settings",
                JsonSerializer.Serialize(
                    new
                    {
                        enable = false,
                        knownProxies = new[] { "203.0.113.10" },
                        knownNetworks = new[] { "10.0.0.0/24" },
                        sharedSecretHeaderName = "X-Test-Proxy-Secret",
                        sharedSecret = "correct-horse-battery-staple",
                        dnsOverTls = new
                        {
                            enable = false,
                            knownProxies = new[] { "198.51.100.5" },
                            knownNetworks = new[] { "172.16.0.0/16" },
                        },
                    }
                )
            },
            {
                "rewrites",
                "/api/rewrite-settings",
                JsonSerializer.Serialize(new { enable = false, timeToLiveSeconds = 300 })
            },
        };

    [Theory]
    [MemberData(nameof(Controllers))]
    public async Task Patch_OfEveryField_OnEachSyncedSettingsController_RecordsOnlyCataloguedKeys(
        string name,
        string route,
        string body
    )
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Patch, route)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            }
        );

        // A key the catalogue lacks makes the recorder throw, which is a 500.
        Assert.True(
            response.IsSuccessStatusCode,
            $"{name}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}"
        );

        // Every field of the body reached the file, so a body field without a catalogue entry
        // cannot hide behind a skipped write.
        JsonObject root = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
        ClusterSyncedSettingsSection section = Section(name);
        HashSet<string> catalogued =
        [
            .. section.Keys.Select(key => $"{key.SectionPath}:{key.Key}".ToLowerInvariant()),
        ];

        foreach (string sectionPath in section.Keys.Select(key => key.SectionPath).Distinct())
        {
            JsonObject fileSection = (JsonObject)
                Ninlil.Web.Configuration.RuntimeConfigurationFileReader.FindSection(
                    root,
                    sectionPath
                );
            Assert.NotNull(fileSection);

            foreach ((string key, JsonNode value) in fileSection)
            {
                // A nested object is another section (DnsServer:Events holds RabbitMq and ActiveMq).
                if (value is JsonObject)
                    continue;

                // The encrypted sibling of a secret is written to the file and never synced.
                if (
                    Ninlil.Web.Configuration.ConfigurationSecretSettings.All.Any(secret =>
                        string.Equals(
                            secret.SectionPath,
                            sectionPath,
                            StringComparison.OrdinalIgnoreCase
                        )
                        && string.Equals(
                            secret.EncryptedKey,
                            key,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                )
                    continue;

                Assert.True(
                    catalogued.Contains($"{sectionPath}:{key}".ToLowerInvariant()),
                    $"{sectionPath}:{key} is written to the settings file but not catalogued."
                );
            }
        }
    }

    /// <summary>
    ///     This method maps a theory case to its catalogue section.
    /// </summary>
    private static ClusterSyncedSettingsSection Section(string name) =>
        ClusterSyncedSettingsCatalog.Find(
            name switch
            {
                "event-broker" => Ninlil
                    .Dns
                    .Server
                    .Core
                    .Events
                    .DnsServerEventPublisherOptions
                    .SectionName,
                "rate-limiting" => Ninlil.Dns.Server.RateLimiting.DnsRateLimitOptions.SectionName,
                "dns-cookies" => Ninlil.Dns.Server.Core.Cookies.DnsCookieOptions.SectionName,
                "reverse-proxy" => Ninlil.Web.Options.ReverseProxyOptions.SectionName,
                "rewrites" => Ninlil.Dns.Server.Rewrites.DnsRewriteOptions.SectionName,
                _ => throw new ArgumentOutOfRangeException(nameof(name)),
            }
        );

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
}
