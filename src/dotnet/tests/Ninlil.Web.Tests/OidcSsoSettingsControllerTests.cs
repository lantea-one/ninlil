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
///     This class covers <see cref="OidcSsoSettingsController" /> end to end, against a real,
///     in-process host and an isolated, throwaway JSON configuration file (see
///     <see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />).
/// </summary>
[Collection(WebTestCollection.Name)]
public class OidcSsoSettingsControllerTests(NinlilDnsWebApplicationFactory factory)
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

        HttpResponseMessage response = await client.GetAsync("/api/sso-settings");
        response.EnsureSuccessStatusCode();

        OidcSsoSettingsDetail detail =
            await response.Content.ReadFromJsonAsync<OidcSsoSettingsDetail>();

        Assert.False(detail.Enable);
        Assert.Equal("Single Sign-On", detail.DisplayName);
        Assert.Equal(string.Empty, detail.Authority);
        Assert.Equal(string.Empty, detail.ClientId);
        Assert.False(detail.ClientSecretConfigured);
        Assert.Equal(new[] { "openid", "profile", "email" }, detail.Scopes);
    }

    [Fact]
    public async Task Patch_WithNewValues_WritesThemToTheConfigurationFileAndEncryptsTheSecret()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/sso-settings",
                new
                {
                    enable = true,
                    displayName = "Sign in with Authelia",
                    authority = "https://auth.example.com",
                    clientId = "ninlil-dns",
                    clientSecret = "correct-horse-battery-staple",
                    scopes = new[] { "openid", "profile", "email", "groups" },
                }
            )
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Contains("restart", result.Note, StringComparison.OrdinalIgnoreCase);

        JsonObject root = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
        JsonObject section = (JsonObject)root["Identity"]["Sso"];

        Assert.True(section["Enable"].GetValue<bool>());
        Assert.Equal("Sign in with Authelia", section["DisplayName"].GetValue<string>());
        Assert.Equal("https://auth.example.com", section["Authority"].GetValue<string>());
        Assert.Equal("ninlil-dns", section["ClientId"].GetValue<string>());

        // The secret written to disk must never be the plaintext this test itself submitted.
        string clientSecretEncrypted = section["ClientSecretEncrypted"].GetValue<string>();
        Assert.NotEqual("correct-horse-battery-staple", clientSecretEncrypted);
        Assert.DoesNotContain("correct-horse-battery-staple", clientSecretEncrypted);

        // This factory's own isolated runtime settings file is deliberately never wired back in
        // as a live configuration source for this same in-process host (the same reasoning
        // AppearanceSettingsControllerTests's own Patch test documents in detail), so there is no
        // "read it back via a live GET" step here -- the configuration-file assertions above are
        // this test's own real, complete proof of what Patch actually wrote.
        JsonArray scopes = (JsonArray)section["Scopes"];
        Assert.Equal(
            new[] { "openid", "profile", "email", "groups" },
            scopes.Select(scope => scope.GetValue<string>())
        );
    }

    [Fact]
    public async Task Patch_WithANonAbsoluteAuthority_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/sso-settings", new { authority = "not-a-url" })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithScopesMissingOpenId_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/sso-settings", new { scopes = new[] { "profile", "email" } })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OidcSsoSettingsEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/sso-settings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
