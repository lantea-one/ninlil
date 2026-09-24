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
///     This class covers <see cref="IdentitySettingsController" /> end to end, against a real,
///     in-process host and an isolated, throwaway JSON configuration file (see
///     <see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />) -- closing a
///     real gap: ASP.NET Core Identity's own built-in password-complexity/lockout
///     policy already drove real, enforced behavior, but had zero admin-facing REST/UI
///     configuration surface.
/// </summary>
[Collection(WebTestCollection.Name)]
public class IdentitySettingsControllerTests(NinlilDnsWebApplicationFactory factory)
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

        HttpResponseMessage response = await client.GetAsync("/api/identity-settings");
        response.EnsureSuccessStatusCode();

        IdentitySettingsDetail detail =
            await response.Content.ReadFromJsonAsync<IdentitySettingsDetail>();

        // ServiceCollectionIdentityExtension's own compiled-in defaults.
        Assert.Equal(12, detail.RequiredLength);
        Assert.True(detail.RequireDigit);
        Assert.True(detail.RequireLowercase);
        Assert.True(detail.RequireUppercase);
        Assert.True(detail.RequireNonAlphanumeric);
        Assert.Equal(TimeSpan.FromMinutes(15), detail.DefaultLockoutTimeSpan);
        Assert.Equal(5, detail.MaxFailedAccessAttempts);
        Assert.True(detail.AllowedForNewUsers);
    }

    [Fact]
    public async Task Patch_WithNewValues_WritesThemToTheConfigurationFile()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        try
        {
            HttpResponseMessage response = await client.SendAsync(
                PatchRequest(
                    "/api/identity-settings",
                    new
                    {
                        requiredLength = 16,
                        maxFailedAccessAttempts = 3,
                        defaultLockoutTimeSpan = "00:30:00",
                    }
                )
            );
            response.EnsureSuccessStatusCode();

            RuntimeSettingsWriteResult result =
                await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
            Assert.Equal(RuntimeSettingsWriteResult.HotReloadedNote, result.Note);

            // This checks only the raw configuration file, never a live GET immediately after
            // this same PATCH -- NinlilDnsWebApplicationFactory's own isolated runtime
            // settings file is never separately registered as its own reload-on-change
            // IConfiguration source, a real, confirmed test-harness limitation this project's
            // settings-controller test suite already found and documented. Genuine "next
            // request" hot reload for IdentityOptions is proved instead, directly, by
            // IdentityOptionsMonitorProxyHotReloadTests (in Ninlil.Dns.Identity.Tests).
            JsonObject root = (JsonObject)
                JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
            JsonObject identity = (JsonObject)root["Identity"];
            Assert.Equal(16, identity["Password"]["RequiredLength"].GetValue<int>());
            Assert.Equal(3, identity["Lockout"]["MaxFailedAccessAttempts"].GetValue<int>());
            Assert.Equal(
                "00:30:00",
                identity["Lockout"]["DefaultLockoutTimeSpan"].GetValue<string>()
            );
        }
        finally
        {
            // This factory's own runtime configuration file is shared across every test in
            // WebTestCollection, and every other test authenticating against the bootstrap
            // administrator's own fixed password depends on the compiled-in lockout/password
            // policy staying in effect -- restore it afterward.
            await client.SendAsync(
                PatchRequest(
                    "/api/identity-settings",
                    new
                    {
                        requiredLength = 12,
                        maxFailedAccessAttempts = 5,
                        defaultLockoutTimeSpan = "00:15:00",
                    }
                )
            );
        }
    }

    [Fact]
    public async Task Patch_WithARequiredLengthOfZero_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/identity-settings", new { requiredLength = 0 })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAnUnparseableLockoutTimeSpan_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/identity-settings",
                new { defaultLockoutTimeSpan = "not-a-timespan" }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAnEmptyBody_ReturnsOkWithoutWritingAnything()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/identity-settings", new { })
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Contains("nothing to change", result.Note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task IdentitySettingsEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/identity-settings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
