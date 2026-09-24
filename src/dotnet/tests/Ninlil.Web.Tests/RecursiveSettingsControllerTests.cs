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
///     This class covers <see cref="RecursiveSettingsController" /> end to end, against a real,
///     in-process host and an isolated, throwaway JSON configuration file (see
///     <see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />) -- closing
///     the single highest-priority gap a full REST/UI configuration-completeness audit found: a
///     fresh install starts with recursion off and, before this controller existed, had no way to
///     turn it on short of hand-editing the configuration file and restarting.
/// </summary>
[Collection(WebTestCollection.Name)]
public class RecursiveSettingsControllerTests(NinlilDnsWebApplicationFactory factory)
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

        HttpResponseMessage response = await client.GetAsync("/api/recursive-settings");
        response.EnsureSuccessStatusCode();

        RecursiveSettingsDetail detail =
            await response.Content.ReadFromJsonAsync<RecursiveSettingsDetail>();

        // The exact, documented, security-conscious defaults -- see
        // RecursiveServerOptions.EnableRecursion's own doc comment for why recursion itself
        // defaults off while DNSSEC validation and automatic trust anchor update both default on.
        Assert.False(detail.EnableRecursion);
        Assert.True(detail.EnableDnssecValidation);
        Assert.True(detail.EnableQnameMinimization);
        Assert.Equal(30, detail.MaxReferralDepth);
        Assert.Equal(15, detail.MaxCnameChainLength);
        Assert.Equal(100, detail.MaxUpstreamQueries);
        Assert.Equal(100_000, detail.MaxCacheEntries);
        Assert.True(detail.EnableAutomaticTrustAnchorUpdate);
        Assert.Equal(TimeSpan.FromDays(30), detail.TrustAnchorRemoveHoldDownDuration);
        Assert.Empty(detail.RootHintServersOverride);
        Assert.Empty(detail.TrustAnchorsOverride);
        Assert.True(detail.EnableServeStale);
        Assert.Equal(TimeSpan.FromHours(24), detail.MaxStaleServeAge);
        Assert.True(detail.EnablePrefetch);
        Assert.Equal(TimeSpan.FromSeconds(10), detail.PrefetchWindow);
        Assert.Equal(TimeSpan.FromMinutes(5), detail.PrefetchRecentAccessWindow);
    }

    [Fact]
    public async Task Patch_WithServeStaleAndPrefetchValues_WritesThemAndReportsHotReload()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/recursive-settings",
                new
                {
                    enableServeStale = false,
                    maxStaleServeAge = "12:00:00",
                    enablePrefetch = false,
                    prefetchWindow = "00:00:20",
                    prefetchRecentAccessWindow = "00:10:00",
                }
            )
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Equal(RuntimeSettingsWriteResult.HotReloadedNote, result.Note);

        JsonObject root = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
        JsonObject section = (JsonObject)root["DnsServer"]["Recursive"];
        Assert.False(section["EnableServeStale"].GetValue<bool>());
        Assert.Equal("12:00:00", section["MaxStaleServeAge"].GetValue<string>());
        Assert.False(section["EnablePrefetch"].GetValue<bool>());
        Assert.Equal("00:00:20", section["PrefetchWindow"].GetValue<string>());
        Assert.Equal("00:10:00", section["PrefetchRecentAccessWindow"].GetValue<string>());
    }

    [Fact]
    public async Task Patch_WithNewScalarValues_WritesThemToTheConfigurationFileAndReportsHotReload()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/recursive-settings",
                new
                {
                    enableRecursion = true,
                    maxReferralDepth = 10,
                    upstreamQueryTimeout = "00:00:02",
                }
            )
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Equal(RuntimeSettingsWriteResult.HotReloadedNote, result.Note);

        JsonObject root = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
        JsonObject section = (JsonObject)root["DnsServer"]["Recursive"];
        Assert.True(section["EnableRecursion"].GetValue<bool>());
        Assert.Equal(10, section["MaxReferralDepth"].GetValue<int>());
        Assert.Equal("00:00:02", section["UpstreamQueryTimeout"].GetValue<string>());
    }

    [Fact]
    public async Task Patch_WithQnameMinimizationAndRemoveHoldDownValues_WritesThemAndReportsHotReload()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/recursive-settings",
                new
                {
                    enableQnameMinimization = false,
                    trustAnchorRemoveHoldDownDuration = "15.00:00:00",
                }
            )
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Equal(RuntimeSettingsWriteResult.HotReloadedNote, result.Note);

        JsonObject root = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
        JsonObject section = (JsonObject)root["DnsServer"]["Recursive"];
        Assert.False(section["EnableQnameMinimization"].GetValue<bool>());
        Assert.Equal(
            "15.00:00:00",
            section["TrustAnchorRemoveHoldDownDuration"].GetValue<string>()
        );
    }

    [Fact]
    public async Task Patch_WithARootHintsOverride_WritesItAndReportsRestartRequired()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/recursive-settings",
                new
                {
                    rootHintServersOverride = new[]
                    {
                        new { name = "private-root.test.", addresses = new[] { "203.0.113.53" } },
                    },
                }
            )
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Equal(RuntimeSettingsWriteResult.RestartRequiredNote, result.Note);

        JsonObject root = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
        JsonArray overrideArray = (JsonArray)
            root["DnsServer"]["Recursive"]["RootHintServersOverride"];
        Assert.Single(overrideArray);
        Assert.Equal("private-root.test.", overrideArray[0]["Name"].GetValue<string>());
        Assert.Equal("203.0.113.53", overrideArray[0]["Addresses"][0].GetValue<string>());
    }

    [Fact]
    public async Task Patch_MixingAHotReloadingFieldAndARestartRequiredField_ReportsTheMixedNote()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/recursive-settings",
                new { enableRecursion = true, rootHintServersOverride = Array.Empty<object>() }
            )
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Equal(RuntimeSettingsWriteResult.MixedReloadNote, result.Note);
    }

    [Fact]
    public async Task Patch_WithAnInvalidDuration_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/recursive-settings", new { overallQueryTimeout = "not-a-timespan" })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAnInvalidTrustAnchorAlgorithm_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/recursive-settings",
                new
                {
                    trustAnchorsOverride = new[]
                    {
                        new
                        {
                            keyTag = 20326,
                            algorithm = "NotARealAlgorithm",
                            digestType = 2,
                            digestHex = "AABBCC",
                        },
                    },
                }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAnInvalidRootHintAddress_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/recursive-settings",
                new
                {
                    rootHintServersOverride = new[]
                    {
                        new { name = "private-root.test.", addresses = new[] { "not-an-ip" } },
                    },
                }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAnEmptyBody_ReturnsOkWithoutWritingAnything()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/recursive-settings", new { })
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Contains("nothing to change", result.Note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RecursiveSettingsEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/recursive-settings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
