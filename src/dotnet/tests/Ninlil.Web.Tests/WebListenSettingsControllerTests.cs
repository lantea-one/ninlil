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
///     This class covers <see cref="WebListenSettingsController" /> (the HTTP and HTTPS listener
///     and its certificate, <c>WebListenOptions</c>) over HTTP on the shared in-process host,
///     signed in as the bootstrap administrator: the startup values, patches checked by reading
///     the host's temporary settings file
///     (<see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />) with the
///     restart note, and input validation. <c>Get</c> always reports the values the listener was
///     set up with at startup (see <see cref="Controllers.WebListenSettingsController" />), so a
///     GET after a PATCH showing the old ports is the expected result here.
/// </summary>
[Collection(WebTestCollection.Name)]
public class WebListenSettingsControllerTests(NinlilDnsWebApplicationFactory factory)
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
    public async Task Get_ReturnsThisTestHostsOwnRandomizedListenerPortsAndTheDefaultCertificateShape()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync("/api/web-listen-settings");
        response.EnsureSuccessStatusCode();

        WebListenSettingsDetail detail =
            await response.Content.ReadFromJsonAsync<WebListenSettingsDetail>();

        // NinlilDnsWebApplicationFactory sets both ports to free ports, so only their range and
        // that they differ are checked, not that they match the factory's values.
        Assert.InRange(detail.HttpPort, 1, 65535);
        Assert.InRange(detail.HttpsPort, 1, 65535);
        Assert.NotEqual(detail.HttpPort, detail.HttpsPort);

        // Every address, unless configured otherwise.
        Assert.Empty(detail.ListenAddresses);

        // The compiled-in defaults of TlsCertificateOptions.
        Assert.Equal("", detail.Certificate.PfxPath);
        Assert.False(detail.Certificate.PfxPasswordConfigured);
        Assert.Equal(["localhost"], detail.Certificate.SelfSignedSubjectAlternativeNames);
        Assert.Empty(detail.Certificate.AllowedProtocols);
    }

    [Fact]
    public async Task Patch_WithNewPortsAndCertificateFields_WritesThemToTheConfigurationFileAndAlwaysReturnsTheRestartRequiredNote()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        try
        {
            HttpResponseMessage response = await client.SendAsync(
                PatchRequest(
                    "/api/web-listen-settings",
                    new
                    {
                        httpPort = 8080,
                        httpsPort = 8443,
                        certificate = new
                        {
                            selfSignedSubjectAlternativeNames = new[] { "dns.example.invalid" },
                            allowedProtocols = new[] { "Tls12", "Tls13" },
                        },
                    }
                )
            );
            response.EnsureSuccessStatusCode();

            RuntimeSettingsWriteResult result =
                await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();

            // Always the restart note, whichever fields were touched, since nothing here
            // hot-reloads (see WebListenSettingsController).
            Assert.Equal(RuntimeSettingsWriteResult.WebListenRestartRequiredNote, result.Note);

            JsonObject root = (JsonObject)
                JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
            JsonObject webSection = (JsonObject)root["DnsServer"]["Web"];
            Assert.Equal(8080, webSection["HttpPort"].GetValue<int>());
            Assert.Equal(8443, webSection["HttpsPort"].GetValue<int>());

            JsonObject certificateSection = (JsonObject)webSection["Certificate"];
            JsonArray subjectAlternativeNames = (JsonArray)
                certificateSection["SelfSignedSubjectAlternativeNames"];
            Assert.Equal(
                ["dns.example.invalid"],
                subjectAlternativeNames.Select(item => item.GetValue<string>())
            );
            Assert.Equal("Tls12, Tls13", certificateSection["AllowedProtocols"].GetValue<string>());

            // The write does not affect the running host: a GET still reports the startup ports,
            // not the values just written.
            HttpResponseMessage liveResponse = await client.GetAsync("/api/web-listen-settings");
            liveResponse.EnsureSuccessStatusCode();
            WebListenSettingsDetail liveDetail =
                await liveResponse.Content.ReadFromJsonAsync<WebListenSettingsDetail>();
            Assert.NotEqual(8080, liveDetail.HttpPort);
            Assert.NotEqual(8443, liveDetail.HttpsPort);
        }
        finally
        {
            // Write the defaults back to the shared settings file.
            await client.SendAsync(
                PatchRequest(
                    "/api/web-listen-settings",
                    new
                    {
                        certificate = new
                        {
                            selfSignedSubjectAlternativeNames = new[] { "localhost" },
                            allowedProtocols = Array.Empty<string>(),
                        },
                    }
                )
            );
        }
    }

    [Fact]
    public async Task Patch_WithAnOutOfRangeHttpPort_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/web-listen-settings", new { httpPort = 70000 })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAnInvalidTlsProtocolName_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/web-listen-settings",
                new { certificate = new { allowedProtocols = new[] { "NotAProtocol" } } }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAnEmptyBody_ReturnsOkWithoutWritingAnything()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/web-listen-settings", new { })
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Contains("nothing to change", result.Note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WebListenSettingsEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/web-listen-settings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithListenAddresses_WritesThemAndRejectsAMalformedOne()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        try
        {
            (
                await client.SendAsync(
                    PatchRequest(
                        "/api/web-listen-settings",
                        new { listenAddresses = new[] { "192.168.1.10", "127.0.0.1" } }
                    )
                )
            ).EnsureSuccessStatusCode();
            Assert.Equal("192.168.1.10, 127.0.0.1", ReadWebListenAddresses());

            HttpResponseMessage invalid = await client.SendAsync(
                PatchRequest("/api/web-listen-settings", new { listenAddresses = new[] { "eth0" } })
            );
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Contains("eth0", await invalid.Content.ReadAsStringAsync());
            Assert.Equal("192.168.1.10, 127.0.0.1", ReadWebListenAddresses());
        }
        finally
        {
            // Only the configuration file changes (the listener applies it on restart), but the
            // file is shared with every other test class, so put the default back.
            (
                await client.SendAsync(
                    PatchRequest(
                        "/api/web-listen-settings",
                        new { listenAddresses = Array.Empty<string>() }
                    )
                )
            ).EnsureSuccessStatusCode();
        }
    }

    /// <summary>
    ///     This method reads the web listener's stored <c>ListenAddresses</c> from this host's
    ///     configuration file, or an empty string when it isn't set.
    /// </summary>
    private string ReadWebListenAddresses()
    {
        JsonObject root = (JsonObject)
            JsonNode.Parse(File.ReadAllText(factory.RuntimeConfigurationFilePath));
        return root["DnsServer"]?["Web"]?["ListenAddresses"]?.GetValue<string>() ?? string.Empty;
    }
}
