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
///     This class covers <see cref="PtrSyncSettingsController" /> (the global default for keeping
///     PTR records in step with forward records; zones override it through <c>ZonesController</c>)
///     over HTTP on the shared in-process host, signed in as the bootstrap administrator: the
///     compiled-in defaults, a patch checked by reading the host's temporary settings file
///     (<see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />) and the
///     returned note.
/// </summary>
[Collection(WebTestCollection.Name)]
public class PtrSyncSettingsControllerTests(NinlilDnsWebApplicationFactory factory)
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
    public async Task Get_ReturnsTheCompiledInDefaultWhenNothingHasBeenConfigured()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync("/api/ptr-sync-settings");
        response.EnsureSuccessStatusCode();

        PtrSyncSettingsDetail detail =
            await response.Content.ReadFromJsonAsync<PtrSyncSettingsDetail>();

        // The compiled-in default: off.
        Assert.False(detail.DefaultEnabled);
    }

    [Fact]
    public async Task Patch_WithDefaultEnabledTrue_WritesItToTheConfigurationFile()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        try
        {
            HttpResponseMessage response = await client.SendAsync(
                PatchRequest("/api/ptr-sync-settings", new { defaultEnabled = true })
            );
            response.EnsureSuccessStatusCode();

            RuntimeSettingsWriteResult result =
                await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
            Assert.Equal(RuntimeSettingsWriteResult.HotReloadedNote, result.Note);

            // Only the file is checked; a GET would still show the old value (see
            // NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath). The hot reload is
            // covered by PtrSyncOptionsHotReloadTests in Ninlil.Dns.Server.Authoritative.Tests.
            JsonObject root = (JsonObject)
                JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
            JsonObject section = (JsonObject)root["DnsServer"]["Authoritative"]["PtrSync"];
            Assert.True(section["DefaultEnabled"].GetValue<bool>());
        }
        finally
        {
            // The settings file is shared with every other test, so write the default back.
            await client.SendAsync(
                PatchRequest("/api/ptr-sync-settings", new { defaultEnabled = false })
            );
        }
    }

    [Fact]
    public async Task Patch_WithAnEmptyBody_ReturnsOkWithoutWritingAnything()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/ptr-sync-settings", new { })
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Contains("nothing to change", result.Note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PtrSyncSettingsEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/ptr-sync-settings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
