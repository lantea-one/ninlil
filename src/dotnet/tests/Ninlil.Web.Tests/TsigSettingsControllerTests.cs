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
///     This class covers <see cref="TsigSettingsController" /> (the clock-skew tolerance, or fudge,
///     in <c>TsigServiceOptions</c>) over HTTP on the shared in-process host, signed in as the
///     bootstrap administrator: the compiled-in default, a patch checked by reading the host's
///     temporary settings file (<see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />)
///     and the returned note, and an out-of-range value.
/// </summary>
[Collection(WebTestCollection.Name)]
public class TsigSettingsControllerTests(NinlilDnsWebApplicationFactory factory)
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

        HttpResponseMessage response = await client.GetAsync("/api/tsig-settings");
        response.EnsureSuccessStatusCode();

        TsigSettingsDetail detail = await response.Content.ReadFromJsonAsync<TsigSettingsDetail>();

        // The compiled-in default, the value RFC 8945 recommends.
        Assert.Equal(300, detail.DefaultFudgeSeconds);
    }

    [Fact]
    public async Task Patch_WithANewFudgeSeconds_WritesItToTheConfigurationFile()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        try
        {
            HttpResponseMessage response = await client.SendAsync(
                PatchRequest("/api/tsig-settings", new { defaultFudgeSeconds = 600 })
            );
            response.EnsureSuccessStatusCode();

            RuntimeSettingsWriteResult result =
                await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
            Assert.Equal(RuntimeSettingsWriteResult.HotReloadedNote, result.Note);

            // Only the file is checked; a GET would still show the old value (see
            // NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath). The hot reload is
            // covered by TsigServiceHotReloadTests in Ninlil.Dns.Server.Tsig.Tests.
            JsonObject root = (JsonObject)
                JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
            JsonObject section = (JsonObject)root["DnsServer"]["Tsig"];
            Assert.Equal(600, section["DefaultFudgeSeconds"].GetValue<int>());
        }
        finally
        {
            // The settings file is shared with every other test, so write the default back.
            await client.SendAsync(
                PatchRequest("/api/tsig-settings", new { defaultFudgeSeconds = 300 })
            );
        }
    }

    [Fact]
    public async Task Patch_WithAnOutOfRangeFudgeSeconds_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/tsig-settings", new { defaultFudgeSeconds = 70000 })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAnEmptyBody_ReturnsOkWithoutWritingAnything()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/tsig-settings", new { })
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Contains("nothing to change", result.Note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TsigSettingsEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/tsig-settings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
