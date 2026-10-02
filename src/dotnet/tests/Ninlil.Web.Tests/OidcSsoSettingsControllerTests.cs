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
///     This class covers <see cref="OidcSsoSettingsController" /> (OIDC single sign-on) over HTTP
///     on the shared in-process host, signed in as the bootstrap administrator: the compiled-in
///     defaults, a patch checked by reading the host's temporary settings file
///     (<see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />) with the
///     client secret not stored in plain text, and input validation. The secret is only checked
///     not to be the plain text, not to decrypt back, and the note only to contain "restart".
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

        // The stored secret must not be the plain text the test sent.
        string clientSecretEncrypted = section["ClientSecretEncrypted"].GetValue<string>();
        Assert.NotEqual("correct-horse-battery-staple", clientSecretEncrypted);
        Assert.DoesNotContain("correct-horse-battery-staple", clientSecretEncrypted);

        // Only the file is checked; a GET would still show the old values (see
        // NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath).
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
