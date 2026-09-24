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
///     This class covers <see cref="AdminLoginRateLimitSettingsController" /> end to end, against
///     a real, in-process host and an isolated, throwaway JSON configuration file (see
///     <see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />) -- the
///     REST/UI surface for this host's own admin-login brute-force limiter's attempt threshold and
///     lockout window. The real lockout/reset/no-op-when-disabled request-time behavior itself is
///     covered by <c>Ninlil.Dns.Identity.Tests.AdminLoginRateLimitingTests</c> instead, against a
///     minimal, dedicated host -- see that class's own doc comment for why this shared fixture
///     cannot exercise that dimension at all.
/// </summary>
[Collection(WebTestCollection.Name)]
public class AdminLoginRateLimitSettingsControllerTests(NinlilDnsWebApplicationFactory factory)
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

    /// <summary>
    ///     This test's own expected <c>Enable</c> value is <see langword="false" />, not this
    ///     class's real compiled-in default of <see langword="true" /> -- see
    ///     <see cref="NinlilDnsWebApplicationFactory" />'s own constructor for why this shared
    ///     fixture deliberately overrides it off. <c>PermitLimit</c>/<c>Window</c> are otherwise
    ///     untouched, so both still reflect their own real compiled-in defaults.
    /// </summary>
    [Fact]
    public async Task Get_ReturnsTheTestFixturesOwnConfiguration()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync(
            "/api/admin-login-rate-limit-settings"
        );
        response.EnsureSuccessStatusCode();

        AdminLoginRateLimitSettingsDetail detail =
            await response.Content.ReadFromJsonAsync<AdminLoginRateLimitSettingsDetail>();

        Assert.False(detail.Enable);
        Assert.Equal(5, detail.PermitLimit);
        Assert.Equal(TimeSpan.FromMinutes(5), detail.Window);
    }

    [Fact]
    public async Task Patch_WithNewValues_WritesThemToTheConfigurationFile()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/admin-login-rate-limit-settings",
                new
                {
                    enable = true,
                    permitLimit = 3,
                    window = "00:02:00",
                }
            )
        );
        response.EnsureSuccessStatusCode();

        JsonObject root = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
        JsonObject section = (JsonObject)root["DnsServer"]["AdminLoginRateLimit"];
        Assert.True(section["Enable"].GetValue<bool>());
        Assert.Equal(3, section["PermitLimit"].GetValue<int>());
        Assert.Equal("00:02:00", section["Window"].GetValue<string>());
    }

    [Fact]
    public async Task Patch_WithAZeroPermitLimit_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/admin-login-rate-limit-settings", new { permitLimit = 0 })
        );

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAnInvalidWindow_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/admin-login-rate-limit-settings", new { window = "not-a-timespan" })
        );

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SettingsEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(
            "/api/admin-login-rate-limit-settings"
        );

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
