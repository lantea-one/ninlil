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
///     This class covers <see cref="ZoneSigningSettingsController" /> (DNSSEC signature timing in
///     <c>ZoneSigningOptions</c>) over HTTP on the shared in-process host, signed in as the
///     bootstrap administrator: the compiled-in defaults, a patch checked by reading the host's
///     temporary settings file (<see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />)
///     and the returned note, and rejection of zero and malformed durations.
/// </summary>
[Collection(WebTestCollection.Name)]
public class ZoneSigningSettingsControllerTests(NinlilDnsWebApplicationFactory factory)
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

        HttpResponseMessage response = await client.GetAsync("/api/zone-signing-settings");
        response.EnsureSuccessStatusCode();

        ZoneSigningSettingsDetail detail =
            await response.Content.ReadFromJsonAsync<ZoneSigningSettingsDetail>();

        // The compiled-in defaults of ZoneSigningOptions.
        Assert.Equal(TimeSpan.FromDays(30), detail.SignatureValidityPeriod);
        Assert.Equal(TimeSpan.FromHours(1), detail.InceptionOffset);
        Assert.Equal(TimeSpan.FromDays(5), detail.RefreshBeforeExpiration);
        Assert.Equal(TimeSpan.FromHours(6), detail.PollInterval);
    }

    [Fact]
    public async Task Patch_WithNewDurations_WritesThemToTheConfigurationFile()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        try
        {
            HttpResponseMessage response = await client.SendAsync(
                PatchRequest(
                    "/api/zone-signing-settings",
                    new
                    {
                        signatureValidityPeriod = "7.00:00:00",
                        inceptionOffset = "02:00:00",
                        refreshBeforeExpiration = "1.00:00:00",
                        pollInterval = "01:00:00",
                    }
                )
            );
            response.EnsureSuccessStatusCode();

            RuntimeSettingsWriteResult result =
                await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
            Assert.Equal(RuntimeSettingsWriteResult.HotReloadedNote, result.Note);

            // Only the file is checked; a GET would still show the old values (see
            // NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath).
            // The hot reload is covered by ZoneSigningOptionsHotReloadTests in
            // Ninlil.Dns.Server.Authoritative.Tests.
            JsonObject root = (JsonObject)
                JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
            JsonObject section = (JsonObject)root["DnsServer"]["Authoritative"]["ZoneSigning"];
            Assert.Equal("7.00:00:00", section["SignatureValidityPeriod"].GetValue<string>());
            Assert.Equal("02:00:00", section["InceptionOffset"].GetValue<string>());
            Assert.Equal("1.00:00:00", section["RefreshBeforeExpiration"].GetValue<string>());
            Assert.Equal("01:00:00", section["PollInterval"].GetValue<string>());
        }
        finally
        {
            // The settings file is shared with every other test, so write the defaults back.
            await client.SendAsync(
                PatchRequest(
                    "/api/zone-signing-settings",
                    new
                    {
                        signatureValidityPeriod = "30.00:00:00",
                        inceptionOffset = "01:00:00",
                        refreshBeforeExpiration = "5.00:00:00",
                        pollInterval = "06:00:00",
                    }
                )
            );
        }
    }

    [Fact]
    public async Task Patch_WithAZeroSignatureValidityPeriod_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/zone-signing-settings", new { signatureValidityPeriod = "00:00:00" })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAnUnparseableDuration_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/zone-signing-settings", new { pollInterval = "not-a-timespan" })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAnEmptyBody_ReturnsOkWithoutWritingAnything()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/zone-signing-settings", new { })
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Contains("nothing to change", result.Note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ZoneSigningSettingsEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/zone-signing-settings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
