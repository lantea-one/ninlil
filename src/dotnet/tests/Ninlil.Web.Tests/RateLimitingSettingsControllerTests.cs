using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="RateLimitingSettingsController" /> (<c>DnsRateLimitOptions</c>)
///     over HTTP on the shared in-process host, signed in as the bootstrap administrator: the
///     compiled-in defaults, patches checked by reading the host's temporary settings file
///     (<see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />), the cluster
///     sync bookkeeping a GET reports after a write, and input validation. The returned note is
///     only checked to contain "restart", which every note does.
/// </summary>
[Collection(WebTestCollection.Name)]
public class RateLimitingSettingsControllerTests(NinlilDnsWebApplicationFactory factory)
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

    private static HttpRequestMessage PatchRequest(string url, object body) =>
        new(HttpMethod.Patch, url)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json"
            ),
        };

    [Fact]
    public async Task Get_ReturnsTheCompiledInDefaultsWhenNothingHasBeenConfigured()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync("/api/rate-limiting-settings");
        response.EnsureSuccessStatusCode();

        RateLimitingSettingsDetail detail =
            await response.Content.ReadFromJsonAsync<RateLimitingSettingsDetail>();

        Assert.False(detail.Enable);
        Assert.Equal(20, detail.QueriesPerSecond);
        Assert.Equal(40, detail.BurstSize);
    }

    [Fact]
    public async Task Patch_WithNewValues_WritesThemToTheConfigurationFile()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/rate-limiting-settings",
                new
                {
                    enable = true,
                    queriesPerSecond = 5.0,
                    burstSize = 10,
                    trackedClientExpiry = "00:10:00",
                }
            )
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Contains("restart", result.Note, StringComparison.OrdinalIgnoreCase);

        JsonObject root = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
        JsonObject section = (JsonObject)root["DnsServer"]["RateLimiting"];
        Assert.True(section["Enable"].GetValue<bool>());
        Assert.Equal(5.0, section["QueriesPerSecond"].GetValue<double>());
        Assert.Equal(10, section["BurstSize"].GetValue<int>());
        Assert.Equal("00:10:00", section["TrackedClientExpiry"].GetValue<string>());
    }

    /// <summary>
    ///     This test checks that the cluster sync bookkeeping follows a write at once, unlike the
    ///     settings values. <see cref="RateLimitingSettingsDetail.LastModifiedUtc" /> and
    ///     <see cref="RateLimitingSettingsDetail.OriginatingNodeId" /> are read from a database row
    ///     (<c>IClusterSyncedSettingsStateRepository</c>), not from options, so a later
    ///     <c>GET</c> shows them even though it still shows the old values (see
    ///     <see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />).
    /// </summary>
    [Fact]
    public async Task Patch_ThenGet_ReportsWhenAndByWhichNodeThisSectionWasLastWritten()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        DateTimeOffset beforeWrite = DateTimeOffset.UtcNow;

        HttpResponseMessage patchResponse = await client.SendAsync(
            PatchRequest("/api/rate-limiting-settings", new { burstSize = 42 })
        );
        patchResponse.EnsureSuccessStatusCode();

        HttpResponseMessage getResponse = await client.GetAsync("/api/rate-limiting-settings");
        getResponse.EnsureSuccessStatusCode();
        RateLimitingSettingsDetail detail =
            await getResponse.Content.ReadFromJsonAsync<RateLimitingSettingsDetail>();

        Assert.NotNull(detail.LastModifiedUtc);
        Assert.True(detail.LastModifiedUtc >= beforeWrite);
        Assert.NotNull(detail.OriginatingNodeId);
    }

    [Fact]
    public async Task Patch_WithSubnetRateLimitingValues_WritesThemToTheConfigurationFile()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/rate-limiting-settings",
                new
                {
                    subnetPrefixLengthIPv4 = 20,
                    subnetUdpQueryLimit = 250,
                    subnetTruncatePercentage = 75.0,
                    subnetBypassCidrs = new[] { "10.0.0.0/8" },
                }
            )
        );
        response.EnsureSuccessStatusCode();

        JsonObject root = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
        JsonObject section = (JsonObject)root["DnsServer"]["RateLimiting"];
        Assert.Equal(20, section["SubnetPrefixLengthIPv4"].GetValue<byte>());
        Assert.Equal(250, section["SubnetUdpQueryLimit"].GetValue<int>());
        Assert.Equal(75.0, section["SubnetTruncatePercentage"].GetValue<double>());
        Assert.Equal("10.0.0.0/8", section["SubnetBypassCidrs"][0].GetValue<string>());
    }

    [Fact]
    public async Task Patch_WithAnInvalidTruncatePercentage_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/rate-limiting-settings", new { subnetTruncatePercentage = 150.0 })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAnInvalidDuration_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/rate-limiting-settings", new { cleanupInterval = "not-a-timespan" })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAnEmptyBody_ReturnsOkWithoutWritingAnything()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/rate-limiting-settings", new { })
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Contains("nothing to change", result.Note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RateLimitingSettingsEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/rate-limiting-settings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
