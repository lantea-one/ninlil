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
///     This class covers <see cref="ForwardingSettingsController" /> (the forwarding switch,
///     failover and health-check tuning, and the forwarded-answer cache) over HTTP on the shared
///     in-process host, signed in as the bootstrap administrator: the compiled-in defaults,
///     patches checked by reading the host's temporary settings file
///     (<see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />) and the
///     returned note, and input validation. A negative or zero health-check interval is not
///     tested.
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

        // Off by default, like the other feature switches (see ForwardingOptions.Enable).
        Assert.False(detail.Enable);
        Assert.Equal(TimeSpan.FromSeconds(30), detail.HealthCheckInterval);
        Assert.Equal(TimeSpan.FromSeconds(3), detail.PerForwarderTimeout);

        // The forwarded-answer cache, unlike forwarding itself, is on by default.
        Assert.True(detail.EnableCache);
        Assert.Equal(10000, detail.MaxCacheEntries);
    }

    [Fact]
    public async Task Patch_WithCacheSettings_WritesThemToTheConfigurationFile()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        try
        {
            HttpResponseMessage response = await client.SendAsync(
                PatchRequest(
                    "/api/forwarding-settings",
                    new { enableCache = false, maxCacheEntries = 2500 }
                )
            );
            response.EnsureSuccessStatusCode();

            JsonObject root = (JsonObject)
                JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
            JsonObject section = (JsonObject)root["DnsServer"]["Forwarding"];
            Assert.False(section["EnableCache"].GetValue<bool>());
            Assert.Equal(2500, section["MaxCacheEntries"].GetValue<int>());
        }
        finally
        {
            // Write the defaults back to the shared settings file.
            await client.SendAsync(
                PatchRequest(
                    "/api/forwarding-settings",
                    new { enableCache = true, maxCacheEntries = 10000 }
                )
            );
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData("lots")]
    public async Task Patch_WithAnInvalidMaxCacheEntries_ReturnsBadRequest(object maxCacheEntries)
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/forwarding-settings", new { maxCacheEntries })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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
            // Turn forwarding off again in the shared settings file. ForwarderQueryResolver runs
            // before recursion, so forwarding left on would change how other tests' queries are
            // answered if the file were ever loaded.
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
