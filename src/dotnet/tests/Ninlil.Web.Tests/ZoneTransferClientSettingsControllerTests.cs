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
///     This class covers <see cref="ZoneTransferClientSettingsController" /> (how a secondary
///     zone pulls AXFR and IXFR, <c>ZoneTransferClientOptions</c>) over HTTP on the shared
///     in-process host, signed in as the bootstrap administrator: the compiled-in defaults, a
///     patch checked by reading the host's temporary settings file
///     (<see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />) and the
///     returned note, and input validation.
/// </summary>
[Collection(WebTestCollection.Name)]
public class ZoneTransferClientSettingsControllerTests(NinlilDnsWebApplicationFactory factory)
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

        HttpResponseMessage response = await client.GetAsync("/api/zone-transfer-client-settings");
        response.EnsureSuccessStatusCode();

        ZoneTransferClientSettingsDetail detail =
            await response.Content.ReadFromJsonAsync<ZoneTransferClientSettingsDetail>();

        // The compiled-in defaults of ZoneTransferClientOptions.
        Assert.Equal(53, detail.PrimaryPort);
        Assert.Equal(TimeSpan.FromSeconds(5), detail.ConnectTimeout);
        Assert.Equal(TimeSpan.FromSeconds(10), detail.ReadTimeout);
        Assert.Equal(TimeSpan.FromSeconds(3), detail.SoaQueryTimeout);
    }

    [Fact]
    public async Task Patch_WithNewValues_WritesThemToTheConfigurationFile()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        try
        {
            HttpResponseMessage response = await client.SendAsync(
                PatchRequest(
                    "/api/zone-transfer-client-settings",
                    new
                    {
                        primaryPort = 5300,
                        connectTimeout = "00:00:08",
                        readTimeout = "00:00:20",
                        soaQueryTimeout = "00:00:06",
                    }
                )
            );
            response.EnsureSuccessStatusCode();

            RuntimeSettingsWriteResult result =
                await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
            Assert.Equal(RuntimeSettingsWriteResult.HotReloadedNote, result.Note);

            // Only the file is checked; a GET would still show the old values (see
            // NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath).
            // The hot reload is covered by TcpZoneTransferClientHotReloadTests in
            // Ninlil.Dns.Server.Authoritative.Tests.
            JsonObject root = (JsonObject)
                JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
            JsonObject section = (JsonObject)
                root["DnsServer"]["Authoritative"]["ZoneTransferClient"];
            Assert.Equal(5300, section["PrimaryPort"].GetValue<int>());
            Assert.Equal("00:00:08", section["ConnectTimeout"].GetValue<string>());
            Assert.Equal("00:00:20", section["ReadTimeout"].GetValue<string>());
            Assert.Equal("00:00:06", section["SoaQueryTimeout"].GetValue<string>());
        }
        finally
        {
            // The settings file is shared with every other test, so write the defaults back.
            await client.SendAsync(
                PatchRequest(
                    "/api/zone-transfer-client-settings",
                    new
                    {
                        primaryPort = 53,
                        connectTimeout = "00:00:05",
                        readTimeout = "00:00:10",
                        soaQueryTimeout = "00:00:03",
                    }
                )
            );
        }
    }

    [Fact]
    public async Task Patch_WithAnOutOfRangePort_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/zone-transfer-client-settings", new { primaryPort = 0 })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAnUnparseableDuration_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/zone-transfer-client-settings",
                new { readTimeout = "not-a-timespan" }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAnEmptyBody_ReturnsOkWithoutWritingAnything()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/zone-transfer-client-settings", new { })
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Contains("nothing to change", result.Note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ZoneTransferClientSettingsEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/zone-transfer-client-settings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
