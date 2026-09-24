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
///     This class covers <see cref="DnssecPolicySettingsController" /> end to end, against a real,
///     in-process host and an isolated, throwaway JSON configuration file (see
///     <see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />) -- closing a
///     real gap: <c>DnssecAlgorithmPolicyOptions</c> already drove a real, enforced
///     restriction inside <c>DnssecKeyManagementService.GenerateKeyAsync</c>, but had zero
///     admin-facing REST/UI configuration surface.
/// </summary>
[Collection(WebTestCollection.Name)]
public class DnssecPolicySettingsControllerTests(NinlilDnsWebApplicationFactory factory)
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

        HttpResponseMessage response = await client.GetAsync("/api/dnssec-policy-settings");
        response.EnsureSuccessStatusCode();

        DnssecPolicySettingsDetail detail =
            await response.Content.ReadFromJsonAsync<DnssecPolicySettingsDetail>();

        // DnssecAlgorithmPolicyOptions' own compiled-in default: no restriction at all.
        Assert.Empty(detail.AllowedAlgorithms);
    }

    [Fact]
    public async Task Patch_WithAllowedAlgorithms_WritesThemToTheConfigurationFile()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        try
        {
            HttpResponseMessage response = await client.SendAsync(
                PatchRequest(
                    "/api/dnssec-policy-settings",
                    new { allowedAlgorithms = new[] { "RSASHA256", "ECDSAP256SHA256" } }
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
            // IOptionsMonitor hot-reload for DnssecAlgorithmPolicyOptions is proved instead,
            // directly, by DnssecAlgorithmPolicyOptionsHotReloadTests (in
            // Ninlil.Dns.Server.Dnssec.Tests).
            JsonObject root = (JsonObject)
                JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
            JsonObject section = (JsonObject)root["DnsServer"]["Dnssec"];
            JsonArray algorithms = (JsonArray)section["AllowedAlgorithms"];
            Assert.Equal(
                ["RsaSha256", "EcdsaP256Sha256"],
                algorithms.Select(item => item.GetValue<string>())
            );
        }
        finally
        {
            // This factory's own runtime configuration file is shared across every test in
            // WebTestCollection -- restore the compiled-in default afterward.
            await client.SendAsync(
                PatchRequest(
                    "/api/dnssec-policy-settings",
                    new { allowedAlgorithms = Array.Empty<string>() }
                )
            );
        }
    }

    [Fact]
    public async Task Patch_WithAnInvalidAlgorithmName_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/dnssec-policy-settings",
                new { allowedAlgorithms = new[] { "NOT-A-REAL-ALGORITHM" } }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAnEmptyBody_ReturnsOkWithoutWritingAnything()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/dnssec-policy-settings", new { })
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Contains("nothing to change", result.Note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DnssecPolicySettingsEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/dnssec-policy-settings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
