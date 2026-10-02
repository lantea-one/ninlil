using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ninlil.Dns.Server.ClientIdentity.Vendor;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="MacVendorSettingsController" /> over HTTP on the shared
///     in-process host, signed in as the bootstrap administrator: the settings and loaded-data
///     report, a merge patch landing in the runtime configuration file, field validation, and an
///     on-demand refresh against unreachable registries that is reported as failed while the
///     embedded snapshot keeps answering lookups.
/// </summary>
[Collection(WebTestCollection.Name)]
public class MacVendorSettingsControllerTests(NinlilDnsWebApplicationFactory factory)
{
    /// <summary>
    ///     This method creates an HTTP client signed in as the bootstrap administrator.
    /// </summary>
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

    /// <summary>
    ///     This method builds a JSON merge-patch request.
    /// </summary>
    private static HttpRequestMessage PatchRequest(object body) =>
        new(HttpMethod.Patch, "/api/mac-vendor-settings")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json"
            ),
        };

    [Fact]
    public async Task Get_ReportsTheSettingsAndTheLoadedEmbeddedSnapshot()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        MacVendorSettingsDetail detail = await client.GetFromJsonAsync<MacVendorSettingsDetail>(
            "/api/mac-vendor-settings"
        );

        // The shared test host turns automatic refresh off and points every registry at an
        // unreachable local URL, both through NINLIL_ environment variables.
        Assert.False(detail.EnableAutomaticRefresh);
        Assert.Null(detail.Status.NextRefreshDueUtc);
        Assert.Equal(
            NinlilDnsWebApplicationFactory.UnreachableMacVendorRegistryUrl,
            detail.MaLRegistryUrl
        );
        Assert.Equal(
            NinlilDnsWebApplicationFactory.UnreachableMacVendorRegistryUrl,
            detail.MaSRegistryUrl
        );
        Assert.True(detail.Status.MaLEntryCount >= 10_000);
        Assert.True(detail.Status.MaMEntryCount >= 1_000);
        Assert.True(detail.Status.MaSEntryCount >= 1_000);
        Assert.Equal(
            detail.Status.MaLEntryCount + detail.Status.MaMEntryCount + detail.Status.MaSEntryCount,
            detail.Status.TotalEntryCount
        );
    }

    [Fact]
    public async Task Patch_WritesEveryAcceptedFieldToTheRuntimeConfigurationFile()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                new
                {
                    refreshInterval = "30.00:00:00",
                    maSRegistryUrl = "https://mirror.example.net/oui36.csv",
                }
            )
        );
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonNode section = JsonNode.Parse(
            await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath)
        )!["DnsServer"]!["MacVendorLookup"]!;
        Assert.Equal("30.00:00:00", section["RefreshInterval"]!.GetValue<string>());
        Assert.Equal(
            "https://mirror.example.net/oui36.csv",
            section["MaSRegistryUrl"]!.GetValue<string>()
        );
    }

    [Theory]
    [InlineData("refreshInterval", "01:00:00")] // under the one-day minimum
    [InlineData("refreshInterval", "soon")]
    [InlineData("maLRegistryUrl", "ftp://standards-oui.ieee.org/oui/oui.csv")]
    [InlineData("maMRegistryUrl", "not a url")]
    public async Task Patch_RejectsAnInvalidValue(string field, string value)
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(new Dictionary<string, object> { [field] = value })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_RejectsANonBooleanEnableFlag()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(new { enableAutomaticRefresh = "yes" })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_AgainstUnreachableRegistries_ReportsFailureAndKeepsTheLoadedData()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        MacVendorSettingsDetail before = await client.GetFromJsonAsync<MacVendorSettingsDetail>(
            "/api/mac-vendor-settings"
        );

        // The test host's registry URLs all point at a port nothing listens on (see
        // NinlilDnsWebApplicationFactory), so this refresh fails fast and never leaves the host.
        HttpResponseMessage response = await client.PostAsync(
            "/api/mac-vendor-settings/refresh",
            null
        );
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        MacVendorDataStatusDetail status =
            await response.Content.ReadFromJsonAsync<MacVendorDataStatusDetail>();

        Assert.Equal(MacVendorRefreshResultEnum.Failed, status.LastRefreshResult);
        Assert.False(string.IsNullOrEmpty(status.LastRefreshError));
        Assert.Equal(before.Status.DataSource, status.DataSource);
        Assert.Equal(before.Status.TotalEntryCount, status.TotalEntryCount);
    }

    [Fact]
    public async Task Endpoints_WithoutAToken_ReturnUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/mac-vendor-settings")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.PostAsync("/api/mac-vendor-settings/refresh", null)).StatusCode
        );
    }
}
