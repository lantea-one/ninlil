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
///     This class covers <see cref="FilteringSettingsController" /> (whether the filter engine
///     runs and how it answers blocked queries) over HTTP on the shared in-process host, signed in
///     as the bootstrap administrator: the compiled-in defaults, patches checked by reading the
///     host's temporary settings file
///     (<see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />) and the
///     returned note, and input validation.
/// </summary>
[Collection(WebTestCollection.Name)]
public class FilteringSettingsControllerTests(NinlilDnsWebApplicationFactory factory)
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

        HttpResponseMessage response = await client.GetAsync("/api/filtering-settings");
        response.EnsureSuccessStatusCode();

        FilteringSettingsDetail detail =
            await response.Content.ReadFromJsonAsync<FilteringSettingsDetail>();

        // Off by default, like the other feature switches (see DnsFilterOptions.Enable).
        Assert.False(detail.Enable);
        Assert.Equal("0.0.0.0", detail.SinkholeAddressV4);
        Assert.Equal("::", detail.SinkholeAddressV6);
        Assert.Equal(60u, detail.SinkholeTimeToLiveSeconds);

        // CNAME-cloaking protection is on by default, since it only applies once filtering is
        // on (see DnsFilterOptions.BlockCnameCloaking).
        Assert.True(detail.BlockCnameCloaking);
    }

    [Fact]
    public async Task Patch_BlockCnameCloaking_IsWrittenToTheConfigurationFile()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        try
        {
            HttpResponseMessage response = await client.SendAsync(
                PatchRequest("/api/filtering-settings", new { blockCnameCloaking = false })
            );
            response.EnsureSuccessStatusCode();

            RuntimeSettingsWriteResult result =
                await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
            Assert.Equal(RuntimeSettingsWriteResult.HotReloadedNote, result.Note);

            JsonObject root = (JsonObject)
                JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
            Assert.False(root["DnsServer"]["Filtering"]["BlockCnameCloaking"].GetValue<bool>());
        }
        finally
        {
            // Write the default back to the shared settings file.
            await client.SendAsync(
                PatchRequest("/api/filtering-settings", new { blockCnameCloaking = true })
            );
        }
    }

    [Fact]
    public async Task Patch_WithNewScalarValues_WritesThemToTheConfigurationFileAndReportsHotReload()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        try
        {
            HttpResponseMessage response = await client.SendAsync(
                PatchRequest(
                    "/api/filtering-settings",
                    new
                    {
                        enable = true,
                        blockResponseMode = "SinkholeAddress",
                        sinkholeAddressV4 = "203.0.113.1",
                        sinkholeAddressV6 = "2001:db8::1",
                        sinkholeTimeToLiveSeconds = 120,
                        precedence = "BlockWins",
                    }
                )
            );
            response.EnsureSuccessStatusCode();

            RuntimeSettingsWriteResult result =
                await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
            Assert.Equal(RuntimeSettingsWriteResult.HotReloadedNote, result.Note);

            JsonObject root = (JsonObject)
                JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
            JsonObject section = (JsonObject)root["DnsServer"]["Filtering"];
            Assert.True(section["Enable"].GetValue<bool>());
            Assert.Equal("SinkholeAddress", section["BlockResponseMode"].GetValue<string>());
            Assert.Equal("203.0.113.1", section["SinkholeAddressV4"].GetValue<string>());
            Assert.Equal("2001:db8::1", section["SinkholeAddressV6"].GetValue<string>());
            Assert.Equal(120u, section["SinkholeTimeToLiveSeconds"].GetValue<uint>());
            Assert.Equal("BlockWins", section["Precedence"].GetValue<string>());
        }
        finally
        {
            // Turn filtering off again in the shared settings file. The other fields written
            // above are left in the file.
            await client.SendAsync(PatchRequest("/api/filtering-settings", new { enable = false }));
        }
    }

    [Fact]
    public async Task Patch_WithAnInvalidBlockResponseMode_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/filtering-settings", new { blockResponseMode = "NotARealMode" })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAnInvalidSinkholeAddressV4_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/filtering-settings", new { sinkholeAddressV4 = "not-an-ip" })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAnIpv6AddressForTheV4Field_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/filtering-settings", new { sinkholeAddressV4 = "::1" })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAnEmptyBody_ReturnsOkWithoutWritingAnything()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/filtering-settings", new { })
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Contains("nothing to change", result.Note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FilteringSettingsEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/filtering-settings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
