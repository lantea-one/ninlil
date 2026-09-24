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
///     This class covers <see cref="WebListenSettingsController" /> end to end, against a real,
///     in-process host and an isolated, throwaway JSON configuration file (see
///     <see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />) -- closing a
///     real gap: <c>WebListenOptions</c> already drove this host's own real Kestrel
///     HTTP/HTTPS binding, but had zero admin-facing REST/UI configuration surface. Unlike every
///     sibling settings controller test class in this project, this one never asserts a live
///     <c>GET</c> reflects a prior <c>PATCH</c> at all -- not because of the usual isolated-file
///     test-harness limitation, but because <see cref="Controllers.WebListenSettingsController" />'s
///     own <c>Get</c> is never live in the first place; see that controller's own doc comment.
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

        // NinlilDnsWebApplicationFactory itself randomizes these two ports (see its own
        // constructor) to keep this test host off privileged/colliding ports -- this asserts only
        // that this controller reports the exact same values Kestrel was actually bound with, not
        // any particular number.
        Assert.InRange(detail.HttpPort, 1, 65535);
        Assert.InRange(detail.HttpsPort, 1, 65535);
        Assert.NotEqual(detail.HttpPort, detail.HttpsPort);

        // TlsCertificateOptions' own compiled-in defaults.
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

            // Always the restart-required note, regardless of which fields were touched --
            // unlike every other settings controller in this project, this one has no
            // hot-reloading path at all to distinguish; see this controller's own doc comment.
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

            // Confirms this write never took effect on this already-running host -- a genuinely
            // live GET against the real, unmodified Kestrel listener still reports the original,
            // randomized ports this factory started with, not the values just written.
            HttpResponseMessage liveResponse = await client.GetAsync("/api/web-listen-settings");
            liveResponse.EnsureSuccessStatusCode();
            WebListenSettingsDetail liveDetail =
                await liveResponse.Content.ReadFromJsonAsync<WebListenSettingsDetail>();
            Assert.NotEqual(8080, liveDetail.HttpPort);
            Assert.NotEqual(8443, liveDetail.HttpsPort);
        }
        finally
        {
            // This factory's own isolated runtime configuration file is shared across every test
            // in WebTestCollection -- restore the compiled-in shape afterward, the same "restore
            // before returning" discipline every other settings controller test class in this
            // project already establishes, even though (unlike every other one) no live host
            // state actually depends on it here.
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
}
