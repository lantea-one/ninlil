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
///     This class covers <see cref="AdminLoginRateLimitSettingsController" /> (the attempt limit
///     and window of the admin-login limiter) over HTTP on the shared in-process host, signed in
///     as the bootstrap administrator. A patch is checked by reading the host's temporary settings
///     file (<see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />). The
///     lockout itself is covered by <c>Ninlil.Dns.Identity.Tests.AdminLoginRateLimitingTests</c>
///     on a dedicated host, because the shared host turns the limiter off.
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
    ///     This test expects <c>Enable</c> to be <see langword="false" />, not the compiled-in
    ///     <see langword="true" />, because the <see cref="NinlilDnsWebApplicationFactory" />
    ///     constructor turns the limiter off. <c>PermitLimit</c> and <c>Window</c> keep their
    ///     compiled-in defaults.
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
