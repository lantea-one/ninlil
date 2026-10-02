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
///     This class covers <see cref="DnssecKeyRolloverSettingsController" /> (the RFC 6781 rollover
///     timing in <c>DnssecKeyRolloverOptions</c>) over HTTP on the shared in-process host, signed
///     in as the bootstrap administrator: the compiled-in defaults, a patch checked by reading the
///     host's temporary settings file
///     (<see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />) and the
///     returned note, and rejection of zero, negative and malformed durations.
/// </summary>
[Collection(WebTestCollection.Name)]
public class DnssecKeyRolloverSettingsControllerTests(NinlilDnsWebApplicationFactory factory)
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

        HttpResponseMessage response = await client.GetAsync("/api/dnssec-key-rollover-settings");
        response.EnsureSuccessStatusCode();

        DnssecKeyRolloverSettingsDetail detail =
            await response.Content.ReadFromJsonAsync<DnssecKeyRolloverSettingsDetail>();

        // The compiled-in defaults of DnssecKeyRolloverOptions.
        Assert.Equal(TimeSpan.FromHours(1), detail.PropagationDelay);
        Assert.Equal(TimeSpan.FromDays(1), detail.ExternalParentDsTtl);
        Assert.Equal(TimeSpan.FromMinutes(15), detail.PollInterval);
    }

    [Fact]
    public async Task Patch_WithNewDurations_WritesThemToTheConfigurationFile()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        try
        {
            HttpResponseMessage response = await client.SendAsync(
                PatchRequest(
                    "/api/dnssec-key-rollover-settings",
                    new
                    {
                        propagationDelay = "02:00:00",
                        externalParentDsTtl = "12:00:00",
                        pollInterval = "00:30:00",
                    }
                )
            );
            response.EnsureSuccessStatusCode();

            RuntimeSettingsWriteResult result =
                await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
            Assert.Equal(RuntimeSettingsWriteResult.HotReloadedNote, result.Note);

            // Only the file is checked; a GET would still show the old values (see
            // NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath).
            JsonObject root = (JsonObject)
                JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
            JsonObject section = (JsonObject)
                root["DnsServer"]["Authoritative"]["DnssecKeyRollover"];
            Assert.Equal("02:00:00", section["PropagationDelay"].GetValue<string>());
            Assert.Equal("12:00:00", section["ExternalParentDsTtl"].GetValue<string>());
            Assert.Equal("00:30:00", section["PollInterval"].GetValue<string>());
        }
        finally
        {
            // The settings file is shared with every other test, so write the defaults back.
            await client.SendAsync(
                PatchRequest(
                    "/api/dnssec-key-rollover-settings",
                    new
                    {
                        propagationDelay = "01:00:00",
                        externalParentDsTtl = "1.00:00:00",
                        pollInterval = "00:15:00",
                    }
                )
            );
        }
    }

    [Fact]
    public async Task Patch_WithAZeroExternalParentDsTtl_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/dnssec-key-rollover-settings",
                new { externalParentDsTtl = "00:00:00" }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithANegativePropagationDelay_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        // TimeSpan.TryParse accepts a leading '-', so this parses as a negative duration and
        // the range check must reject it.
        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/dnssec-key-rollover-settings",
                new { propagationDelay = "-01:00:00" }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAnUnparseableDuration_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/dnssec-key-rollover-settings",
                new { pollInterval = "not-a-timespan" }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAnEmptyBody_ReturnsOkWithoutWritingAnything()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/dnssec-key-rollover-settings", new { })
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Contains("nothing to change", result.Note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DnssecKeyRolloverSettingsEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/dnssec-key-rollover-settings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
