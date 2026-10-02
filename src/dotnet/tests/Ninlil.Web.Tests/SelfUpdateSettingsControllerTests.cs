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
///     This class covers <see cref="SelfUpdateSettingsController" /> (the release feed and check
///     interval) over HTTP on the shared in-process host, signed in as the bootstrap
///     administrator: the compiled-in defaults, a patch checked by reading the host's temporary
///     settings file (<see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />)
///     and the returned note, and input validation.
/// </summary>
[Collection(WebTestCollection.Name)]
public class SelfUpdateSettingsControllerTests(NinlilDnsWebApplicationFactory factory)
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

        HttpResponseMessage response = await client.GetAsync("/api/self-update-settings");
        response.EnsureSuccessStatusCode();

        SelfUpdateSettingsDetail detail =
            await response.Content.ReadFromJsonAsync<SelfUpdateSettingsDetail>();

        Assert.True(detail.EnableAutomaticCheck);
        Assert.Equal(string.Empty, detail.ReleaseFeedUrl);
        Assert.Equal(TimeSpan.FromHours(24), detail.CheckInterval);
    }

    [Fact]
    public async Task Patch_WithANewFeedUrl_WritesItToTheConfigurationFile()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/self-update-settings",
                new { releaseFeedUrl = "https://example.com/ninlil/releases.json" }
            )
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Contains("no restart", result.Note, StringComparison.OrdinalIgnoreCase);

        JsonObject root = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
        JsonObject section = (JsonObject)root["DnsServer"]["SelfUpdate"];
        Assert.Equal(
            "https://example.com/ninlil/releases.json",
            section["ReleaseFeedUrl"].GetValue<string>()
        );
    }

    [Fact]
    public async Task Patch_WithAMalformedFeedUrl_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/self-update-settings", new { releaseFeedUrl = "not a url" })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAFileSchemeUrl_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        // Only http and https are allowed; a file:// URL is a valid absolute URI but must still
        // be rejected.
        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/self-update-settings", new { releaseFeedUrl = "file:///etc/passwd" })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAnInvalidCheckInterval_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/self-update-settings", new { checkInterval = "not-a-timespan" })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SelfUpdateSettingsEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/self-update-settings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
