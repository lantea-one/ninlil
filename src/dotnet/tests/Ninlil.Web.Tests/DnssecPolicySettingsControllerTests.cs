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
///     This class covers <see cref="DnssecPolicySettingsController" /> (the allowed algorithms in
///     <c>DnssecAlgorithmPolicyOptions</c>, enforced by
///     <c>DnssecKeyManagementService.GenerateKeyAsync</c>) over HTTP on the shared in-process host,
///     signed in as the bootstrap administrator: the compiled-in default, a patch checked by
///     reading the host's temporary settings file
///     (<see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />), and an
///     unknown algorithm name.
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

        // The compiled-in default: no restriction.
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

            // Only the file is checked; a GET would still show the old values (see
            // NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath). The hot reload is
            // covered by DnssecAlgorithmPolicyOptionsHotReloadTests in Ninlil.Dns.Server.Dnssec.Tests.
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
            // The settings file is shared with every other test, so write the default back.
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

    [Theory]
    [InlineData("RSAMD5")]
    [InlineData("DSA")]
    [InlineData("RSASHA1")]
    [InlineData("DSA-NSEC3-SHA1")]
    [InlineData("RSASHA1-NSEC3-SHA1")]
    public async Task Patch_WithAnAlgorithmProhibitedForSigning_ReturnsBadRequest(string algorithm)
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/dnssec-policy-settings",
                new { allowedAlgorithms = new[] { "ECDSAP256SHA256", algorithm } }
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
