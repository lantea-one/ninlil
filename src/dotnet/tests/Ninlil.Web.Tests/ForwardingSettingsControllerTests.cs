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
///     This class covers <see cref="ForwardingSettingsController" /> end to end, against a real,
///     in-process host and an isolated, throwaway JSON configuration file (see
///     <see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />) -- closing a
///     real gap: forwarder entries already existed via <c>ForwardersController</c>, but
///     the engine-level toggle and failover/health-check tuning had no REST/UI surface until this
///     controller.
/// </summary>
[Collection(WebTestCollection.Name)]
public class ForwardingSettingsControllerTests(NinlilDnsWebApplicationFactory factory)
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

        HttpResponseMessage response = await client.GetAsync("/api/forwarding-settings");
        response.EnsureSuccessStatusCode();

        ForwardingSettingsDetail detail =
            await response.Content.ReadFromJsonAsync<ForwardingSettingsDetail>();

        // Off by default, the same "capability-expanding toggle" convention every other engine
        // toggle in this project follows -- see ForwardingOptions.Enable's own doc comment.
        Assert.False(detail.Enable);
        Assert.Equal(TimeSpan.FromSeconds(30), detail.HealthCheckInterval);
        Assert.Equal(TimeSpan.FromSeconds(3), detail.PerForwarderTimeout);
    }

    [Fact]
    public async Task Patch_WithNewScalarValues_WritesThemToTheConfigurationFileAndReportsHotReload()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        try
        {
            HttpResponseMessage response = await client.SendAsync(
                PatchRequest(
                    "/api/forwarding-settings",
                    new
                    {
                        enable = true,
                        failoverStrategy = "TryInOrder",
                        healthCheckInterval = "00:00:45",
                        perForwarderTimeout = "00:00:05",
                    }
                )
            );
            response.EnsureSuccessStatusCode();

            RuntimeSettingsWriteResult result =
                await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
            Assert.Equal(RuntimeSettingsWriteResult.HotReloadedNote, result.Note);

            JsonObject root = (JsonObject)
                JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
            JsonObject section = (JsonObject)root["DnsServer"]["Forwarding"];
            Assert.True(section["Enable"].GetValue<bool>());
            Assert.Equal("TryInOrder", section["FailoverStrategy"].GetValue<string>());
            Assert.Equal("00:00:45", section["HealthCheckInterval"].GetValue<string>());
            Assert.Equal("00:00:05", section["PerForwarderTimeout"].GetValue<string>());
        }
        finally
        {
            // This factory's own runtime configuration file is shared across every test in
            // WebTestCollection (see that collection's own doc comment), and -- unlike filtering
            // or rewrites -- ForwarderQueryResolver is tried before this server's own recursive
            // resolution, so leaving forwarding enabled could change how an unrelated test's own
            // query is answered. Restored afterward, the same "restore before returning"
            // discipline ServicesControllerCrudTests already establishes.
            await client.SendAsync(
                PatchRequest("/api/forwarding-settings", new { enable = false })
            );
        }
    }

    [Fact]
    public async Task Patch_WithAnInvalidFailoverStrategy_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/forwarding-settings", new { failoverStrategy = "NotARealStrategy" })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAnInvalidDuration_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/forwarding-settings", new { perForwarderTimeout = "not-a-timespan" })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAnEmptyBody_ReturnsOkWithoutWritingAnything()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/forwarding-settings", new { })
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Contains("nothing to change", result.Note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ForwardingSettingsEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/forwarding-settings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
