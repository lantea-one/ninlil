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
///     This class covers <see cref="Dns64SettingsController" /> over HTTP on the shared in-process
///     host, signed in as the bootstrap administrator: the compiled-in defaults, a patch checked
///     by reading the host's temporary settings file
///     (<see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />), and rejection
///     of prefixes RFC 6052 does not allow.
/// </summary>
[Collection(WebTestCollection.Name)]
public class Dns64SettingsControllerTests(NinlilDnsWebApplicationFactory factory)
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

        HttpResponseMessage response = await client.GetAsync("/api/dns64-settings");
        response.EnsureSuccessStatusCode();

        Dns64SettingsDetail detail =
            await response.Content.ReadFromJsonAsync<Dns64SettingsDetail>();

        Assert.False(detail.Enable);
        Assert.Equal("64:ff9b::/96", detail.SynthesisPrefix);
    }

    [Fact]
    public async Task Patch_WithNewValues_WritesThemToTheConfigurationFile()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/dns64-settings",
                new { enable = true, synthesisPrefix = "2001:db8:64::/96" }
            )
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Contains("no restart", result.Note, StringComparison.OrdinalIgnoreCase);

        JsonObject root = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
        JsonObject section = (JsonObject)root["DnsServer"]["Dns64"];
        Assert.True(section["Enable"].GetValue<bool>());
        Assert.Equal("2001:db8:64::/96", section["SynthesisPrefix"].GetValue<string>());
    }

    [Fact]
    public async Task Patch_WithAnIllegalPrefixLength_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        // /100 is not one of RFC 6052 §2.2's six legal prefix lengths (32/40/48/56/64/96).
        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/dns64-settings", new { synthesisPrefix = "2001:db8::/100" })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAnIPv4Prefix_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        // TryParsePrefix requires an IPv6 CIDR; an IPv4 one is never valid here.
        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/dns64-settings", new { synthesisPrefix = "192.0.2.0/24" })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Dns64SettingsEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/dns64-settings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
