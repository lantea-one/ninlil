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
///     This class covers <see cref="RewriteSettingsController" /> end to end, against a real,
///     in-process host and an isolated, throwaway JSON configuration file (see
///     <see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />) -- closing a
///     real gap: rewrite entries already existed via <c>DnsRewritesController</c>, but
///     the engine-level toggle had no REST/UI surface until this controller.
/// </summary>
[Collection(WebTestCollection.Name)]
public class RewriteSettingsControllerTests(NinlilDnsWebApplicationFactory factory)
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

        HttpResponseMessage response = await client.GetAsync("/api/rewrite-settings");
        response.EnsureSuccessStatusCode();

        RewriteSettingsDetail detail =
            await response.Content.ReadFromJsonAsync<RewriteSettingsDetail>();

        // Off by default, the same "capability-expanding toggle" convention every other engine
        // toggle in this project follows -- see DnsRewriteOptions.Enable's own doc comment.
        Assert.False(detail.Enable);
        Assert.Equal(300u, detail.TimeToLiveSeconds);
    }

    [Fact]
    public async Task Patch_WithNewScalarValues_WritesThemToTheConfigurationFileAndReportsHotReload()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        try
        {
            HttpResponseMessage response = await client.SendAsync(
                PatchRequest(
                    "/api/rewrite-settings",
                    new { enable = true, timeToLiveSeconds = 900 }
                )
            );
            response.EnsureSuccessStatusCode();

            RuntimeSettingsWriteResult result =
                await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
            Assert.Equal(RuntimeSettingsWriteResult.HotReloadedNote, result.Note);

            JsonObject root = (JsonObject)
                JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
            JsonObject section = (JsonObject)root["DnsServer"]["Rewrites"];
            Assert.True(section["Enable"].GetValue<bool>());
            Assert.Equal(900u, section["TimeToLiveSeconds"].GetValue<uint>());
        }
        finally
        {
            // This factory's own runtime configuration file is shared across every test in
            // WebTestCollection (see that collection's own doc comment) -- restore rewrites back
            // to off afterward, the same "restore before returning" discipline
            // ServicesControllerCrudTests already establishes for exactly this shared-state
            // concern, so no other test in the collection observes rewrites left on.
            await client.SendAsync(PatchRequest("/api/rewrite-settings", new { enable = false }));
        }
    }

    [Fact]
    public async Task Patch_WithAnEmptyBody_ReturnsOkWithoutWritingAnything()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/rewrite-settings", new { })
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Contains("nothing to change", result.Note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RewriteSettingsEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/rewrite-settings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
