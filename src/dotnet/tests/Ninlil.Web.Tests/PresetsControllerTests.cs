using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Ninlil.Web.Controllers;
using Ninlil.Web.Examples.Presets;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="PresetsController" /> over HTTP on the shared in-process host,
///     signed in as the bootstrap administrator: listing the five built-in presets, applying one
///     through <c>PresetApplier</c> (checked by reading two of its settings back from the host's
///     temporary settings file, <see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />),
///     an unknown preset name, and rejection without a token.
/// </summary>
[Collection(WebTestCollection.Name)]
public class PresetsControllerTests(NinlilDnsWebApplicationFactory factory)
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

    [Fact]
    public async Task List_MatchesTheSwaggerExample()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync("/api/presets");
        response.EnsureSuccessStatusCode();

        List<PresetSummary> presets = await response.Content.ReadFromJsonAsync<
            List<PresetSummary>
        >();

        Assert.Equal(new PresetSummaryListExample().GetExamples(), presets);
    }

    [Fact]
    public async Task List_ReturnsAllFiveBuiltInPresets()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync("/api/presets");
        response.EnsureSuccessStatusCode();

        List<PresetSummary> presets = await response.Content.ReadFromJsonAsync<
            List<PresetSummary>
        >();

        Assert.Equal(5, presets.Count);
        Assert.Contains(presets, preset => preset.Name == "Open");
        Assert.Contains(presets, preset => preset.Name == "Baseline");
        Assert.Contains(presets, preset => preset.Name == "Protective");
        Assert.Contains(presets, preset => preset.Name == "Family");
        Assert.Contains(presets, preset => preset.Name == "FIPS");
    }

    [Fact]
    public async Task Apply_WithAKnownPreset_WritesItsSettingsToTheConfigurationFileAndReturns200()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsync("/api/presets/Baseline/apply", null);
        response.EnsureSuccessStatusCode();

        PresetApplyResult result = await response.Content.ReadFromJsonAsync<PresetApplyResult>();
        Assert.Equal("Baseline", result.Name);
        Assert.False(string.IsNullOrWhiteSpace(result.Note));

        JsonObject root = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
        Assert.True(root["DnsServer"]["RateLimiting"]["Enable"].GetValue<bool>());
        Assert.True(root["DnsServer"]["Cookies"]["Enable"].GetValue<bool>());
    }

    [Fact]
    public async Task Apply_WithAnUnknownPresetName_ReturnsNotFound()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsync(
            "/api/presets/DoesNotExist/apply",
            null
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PresetsEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/presets");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
