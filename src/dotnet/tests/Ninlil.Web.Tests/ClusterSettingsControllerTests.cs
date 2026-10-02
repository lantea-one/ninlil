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
///     This class covers <see cref="ClusterSettingsController" /> over HTTP on the shared
///     in-process host, signed in as the bootstrap administrator: the compiled-in defaults,
///     patches checked by reading the host's temporary settings file
///     (<see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />) together with
///     the returned reload note, and input validation.
/// </summary>
[Collection(WebTestCollection.Name)]
public class ClusterSettingsControllerTests(NinlilDnsWebApplicationFactory factory)
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

        HttpResponseMessage response = await client.GetAsync("/api/cluster-settings");
        response.EnsureSuccessStatusCode();

        ClusterSettingsDetail detail =
            await response.Content.ReadFromJsonAsync<ClusterSettingsDetail>();

        Assert.False(detail.Enable);
        Assert.NotEqual(Guid.Empty, detail.NodeId);
        Assert.Equal("", detail.AdvertisedHost);
        Assert.Equal(0, detail.AdvertisedPort);
        Assert.Equal(TimeSpan.FromSeconds(30), detail.HeartbeatInterval);
        Assert.Equal(TimeSpan.FromSeconds(10), detail.HeartbeatTimeout);
        Assert.Equal(3, detail.MissedHeartbeatThreshold);

        // The compiled-in default of CatalogSyncOptions, which this controller also edits (see
        // ClusterSettingsDetail).
        Assert.Equal(TimeSpan.FromMinutes(1), detail.AddressReconcileInterval);
    }

    [Fact]
    public async Task Patch_WithNewValues_WritesThemToTheConfigurationFile()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/cluster-settings",
                new
                {
                    enable = true,
                    advertisedHost = "10.0.1.10",
                    missedHeartbeatThreshold = 5,
                    addressReconcileInterval = "00:02:00",
                }
            )
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        // The patch has a hot-reloading field (missedHeartbeatThreshold) and one that needs a
        // restart (addressReconcileInterval, which CatalogClusterAddressSyncService reads into its
        // PeriodicTimer at startup, as with ClusterOptions.HeartbeatInterval), so the mixed note
        // is expected.
        Assert.Equal(RuntimeSettingsWriteResult.MixedReloadNote, result.Note);

        JsonObject root = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
        JsonObject section = (JsonObject)root["Cluster"];
        Assert.True(section["Enable"].GetValue<bool>());
        Assert.Equal("10.0.1.10", section["AdvertisedHost"].GetValue<string>());
        Assert.Equal(5, section["MissedHeartbeatThreshold"].GetValue<int>());

        // AddressReconcileInterval lives in the top-level "CatalogSync" section
        // (CatalogSyncOptions.SectionName), not under "Cluster".
        JsonObject catalogSyncSection = (JsonObject)root["CatalogSync"];
        Assert.Equal("00:02:00", catalogSyncSection["AddressReconcileInterval"].GetValue<string>());
    }

    [Fact]
    public async Task Patch_WithAnInvalidMissedHeartbeatThreshold_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/cluster-settings", new { missedHeartbeatThreshold = 0 })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAMalformedHeartbeatInterval_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/cluster-settings", new { heartbeatInterval = "not-a-timespan" })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAZeroAddressReconcileInterval_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/cluster-settings", new { addressReconcileInterval = "00:00:00" })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithOnlyAnAddressReconcileInterval_ReturnsARestartRequiredNote()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/cluster-settings", new { addressReconcileInterval = "00:05:00" })
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Equal(RuntimeSettingsWriteResult.RestartRequiredNote, result.Note);
    }

    [Fact]
    public async Task ClusterSettingsEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/cluster-settings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
