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
///     This class covers <see cref="OutboundQuerySettingsController" /> (the source-port pool for
///     outgoing queries) over HTTP on the shared in-process host, signed in as the bootstrap
///     administrator: the compiled-in defaults, a patch checked by reading the host's temporary
///     settings file (<see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />)
///     and the returned note, and rejection without a token.
/// </summary>
[Collection(WebTestCollection.Name)]
public class OutboundQuerySettingsControllerTests(NinlilDnsWebApplicationFactory factory)
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

        HttpResponseMessage response = await client.GetAsync("/api/outbound-query-settings");
        response.EnsureSuccessStatusCode();

        OutboundQuerySettingsDetail detail =
            await response.Content.ReadFromJsonAsync<OutboundQuerySettingsDetail>();

        Assert.False(detail.EnableSourcePortPool);
        Assert.Equal(128, detail.PoolSizePerAddressFamily);
        Assert.Empty(detail.ExcludedSourcePorts);
    }

    [Fact]
    public async Task Patch_WithPoolSizeAndExcludedPorts_WritesThemAndReturnsTheMixedNote()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        try
        {
            HttpResponseMessage response = await client.SendAsync(
                PatchRequest(
                    "/api/outbound-query-settings",
                    new { poolSizePerAddressFamily = 64, excludedSourcePorts = new[] { 51820 } }
                )
            );
            response.EnsureSuccessStatusCode();

            RuntimeSettingsWriteResult result =
                await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
            Assert.Equal(RuntimeSettingsWriteResult.OutboundQueryMixedReloadNote, result.Note);

            JsonObject root = (JsonObject)
                JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
            JsonObject section = (JsonObject)root["DnsServer"]["OutboundQuery"];
            Assert.Equal(64, section["PoolSizePerAddressFamily"].GetValue<int>());
            Assert.Equal(51820, section["ExcludedSourcePorts"][0].GetValue<int>());
        }
        finally
        {
            await RestoreDefaultsAsync(client);
        }
    }

    [Fact]
    public async Task Patch_WithOnlyThePoolSize_ReturnsTheRestartRequiredNote()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        try
        {
            HttpResponseMessage response = await client.SendAsync(
                PatchRequest("/api/outbound-query-settings", new { poolSizePerAddressFamily = 64 })
            );
            response.EnsureSuccessStatusCode();

            RuntimeSettingsWriteResult result =
                await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
            Assert.Equal(
                RuntimeSettingsWriteResult.OutboundQueryPoolSizeRestartRequiredNote,
                result.Note
            );
        }
        finally
        {
            await RestoreDefaultsAsync(client);
        }
    }

    [Fact]
    public async Task Patch_WithOnlyLiveFields_ReturnsTheHotReloadedNote()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        try
        {
            HttpResponseMessage response = await client.SendAsync(
                PatchRequest(
                    "/api/outbound-query-settings",
                    new { enableSourcePortPool = true, excludedSourcePorts = new[] { 51820 } }
                )
            );
            response.EnsureSuccessStatusCode();

            RuntimeSettingsWriteResult result =
                await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
            Assert.Equal(RuntimeSettingsWriteResult.HotReloadedNote, result.Note);
        }
        finally
        {
            await RestoreDefaultsAsync(client);
        }
    }

    /// <summary>
    ///     This method writes the compiled-in defaults back so the shared host's later tests see
    ///     them.
    /// </summary>
    private static Task<HttpResponseMessage> RestoreDefaultsAsync(HttpClient client) =>
        client.SendAsync(
            PatchRequest(
                "/api/outbound-query-settings",
                new
                {
                    enableSourcePortPool = false,
                    poolSizePerAddressFamily = 128,
                    excludedSourcePorts = Array.Empty<int>(),
                }
            )
        );

    [Fact]
    public async Task OutboundQuerySettingsEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/outbound-query-settings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
