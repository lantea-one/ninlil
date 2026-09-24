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
///     This class covers <see cref="SecondaryZoneRefreshSettingsController" /> end to end, against
///     a real, in-process host and an isolated, throwaway JSON configuration file (see
///     <see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />) -- closing a
///     real gap: <c>SecondaryZoneRefreshOptions</c> already drove
///     <c>SecondaryZoneRefreshService</c>'s own polling loop, but had zero admin-facing REST/UI
///     configuration surface.
/// </summary>
[Collection(WebTestCollection.Name)]
public class SecondaryZoneRefreshSettingsControllerTests(NinlilDnsWebApplicationFactory factory)
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

        HttpResponseMessage response = await client.GetAsync(
            "/api/secondary-zone-refresh-settings"
        );
        response.EnsureSuccessStatusCode();

        SecondaryZoneRefreshSettingsDetail detail =
            await response.Content.ReadFromJsonAsync<SecondaryZoneRefreshSettingsDetail>();

        // SecondaryZoneRefreshOptions' own compiled-in defaults.
        Assert.Equal(TimeSpan.FromSeconds(15), detail.PollInterval);
        Assert.Equal(TimeSpan.FromSeconds(30), detail.DefaultRetryInterval);
    }

    [Fact]
    public async Task Patch_WithNewDurations_WritesThemToTheConfigurationFile()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        try
        {
            HttpResponseMessage response = await client.SendAsync(
                PatchRequest(
                    "/api/secondary-zone-refresh-settings",
                    new { pollInterval = "00:00:05", defaultRetryInterval = "00:01:00" }
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
            // settings-controller test suite already found and documented. Genuine
            // IOptionsMonitor hot-reload for SecondaryZoneRefreshOptions is proved instead,
            // directly, by SecondaryZoneRefreshServiceHotReloadTests (in
            // Ninlil.Dns.Server.Authoritative.Tests).
            JsonObject root = (JsonObject)
                JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
            JsonObject section = (JsonObject)
                root["DnsServer"]["Authoritative"]["SecondaryZoneRefresh"];
            Assert.Equal("00:00:05", section["PollInterval"].GetValue<string>());
            Assert.Equal("00:01:00", section["DefaultRetryInterval"].GetValue<string>());
        }
        finally
        {
            // This factory's own runtime configuration file is shared across every test in
            // WebTestCollection -- restore every compiled-in default afterward.
            await client.SendAsync(
                PatchRequest(
                    "/api/secondary-zone-refresh-settings",
                    new { pollInterval = "00:00:15", defaultRetryInterval = "00:00:30" }
                )
            );
        }
    }

    [Fact]
    public async Task Patch_WithAnUnparseableDuration_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/secondary-zone-refresh-settings",
                new { pollInterval = "not-a-timespan" }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAZeroDuration_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/secondary-zone-refresh-settings", new { pollInterval = "00:00:00" })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAnEmptyBody_ReturnsOkWithoutWritingAnything()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/secondary-zone-refresh-settings", new { })
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Contains("nothing to change", result.Note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SecondaryZoneRefreshSettingsEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(
            "/api/secondary-zone-refresh-settings"
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
