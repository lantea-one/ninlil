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
///     This class covers <see cref="ReverseProxySettingsController" /> over HTTP on the shared
///     in-process host, signed in as the bootstrap administrator: the compiled-in defaults,
///     patches checked by reading the host's temporary settings file
///     (<see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />), keeping the
///     shared secret out of responses, and input validation. The trust decision of
///     <see cref="Middleware.ReverseProxyTrustGatingMiddleware" /> is tested on a constructed
///     <see cref="Microsoft.AspNetCore.Http.DefaultHttpContext" /> in
///     <see cref="ReverseProxyTrustGatingMiddlewareTests" />, because the middleware is only added
///     when <see cref="Options.ReverseProxyOptions.Enable" /> is on at startup, and
///     <c>TestServer</c> leaves <c>HttpContext.Connection.RemoteIpAddress</c> null for every
///     request.
/// </summary>
[Collection(WebTestCollection.Name)]
public class ReverseProxySettingsControllerTests(NinlilDnsWebApplicationFactory factory)
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

        HttpResponseMessage response = await client.GetAsync("/api/reverse-proxy-settings");
        response.EnsureSuccessStatusCode();

        ReverseProxySettingsDetail detail =
            await response.Content.ReadFromJsonAsync<ReverseProxySettingsDetail>();

        Assert.False(detail.Enable);
        Assert.Empty(detail.KnownProxies);
        Assert.Empty(detail.KnownNetworks);
        Assert.False(detail.SharedSecretConfigured);
        Assert.NotNull(detail.DnsOverTls);
        Assert.False(detail.DnsOverTls.Enable);
        Assert.Empty(detail.DnsOverTls.KnownProxies);
        Assert.Empty(detail.DnsOverTls.KnownNetworks);
    }

    [Fact]
    public async Task Patch_WithNewValues_WritesThemToTheConfigurationFile()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/reverse-proxy-settings",
                new
                {
                    enable = true,
                    knownProxies = new[] { "203.0.113.10" },
                    knownNetworks = new[] { "10.0.0.0/24" },
                    sharedSecretHeaderName = "X-Test-Proxy-Secret",
                    sharedSecret = "correct-horse-battery-staple",
                }
            )
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Contains("restart", result.Note, StringComparison.OrdinalIgnoreCase);

        JsonObject root = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
        JsonObject section = (JsonObject)root["DnsServer"]["ReverseProxy"];
        Assert.True(section["Enable"].GetValue<bool>());
        Assert.Equal("203.0.113.10", section["KnownProxies"][0].GetValue<string>());
        Assert.Equal("10.0.0.0/24", section["KnownNetworks"][0].GetValue<string>());
        Assert.Equal("X-Test-Proxy-Secret", section["SharedSecretHeaderName"].GetValue<string>());
        Assert.Equal("correct-horse-battery-staple", section["SharedSecret"].GetValue<string>());
    }

    [Fact]
    public async Task Patch_WithDnsOverTlsValues_WritesThemToTheSeparateProxyProtocolSection()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/reverse-proxy-settings",
                new
                {
                    dnsOverTls = new
                    {
                        enable = true,
                        knownProxies = new[] { "198.51.100.5" },
                        knownNetworks = new[] { "172.16.0.0/16" },
                    },
                }
            )
        );
        response.EnsureSuccessStatusCode();

        JsonObject root = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
        JsonObject dnsOverTlsSection = (JsonObject)
            root["DnsServer"]["Protocol"]["DnsOverTls"]["ProxyProtocol"];
        Assert.True(dnsOverTlsSection["Enable"].GetValue<bool>());
        Assert.Equal("198.51.100.5", dnsOverTlsSection["KnownProxies"][0].GetValue<string>());
        Assert.Equal("172.16.0.0/16", dnsOverTlsSection["KnownNetworks"][0].GetValue<string>());

        // A patch naming only dnsOverTls must leave the HTTP-side "ReverseProxy" section alone.
        // That section may or may not exist yet, depending on test order (xUnit orders test cases
        // by a hash of their names), so a missing section passes too. If
        // Patch_WithNewValues_WritesThemToTheConfigurationFile ran first, its "Enable" key would
        // already be in the shared file and this assertion would fail, so the test depends on
        // running before that one.
        JsonObject httpSection = root["DnsServer"]?["ReverseProxy"] as JsonObject;
        Assert.False(httpSection?.ContainsKey("Enable") ?? false);
    }

    [Fact]
    public async Task Patch_WithAMalformedDnsOverTlsKnownProxyAddress_ReturnsBadRequestAndWritesNothing()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/reverse-proxy-settings",
                new { dnsOverTls = new { knownProxies = new[] { "not-an-ip-address" } } }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAMalformedDnsOverTlsKnownNetwork_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/reverse-proxy-settings",
                new { dnsOverTls = new { knownNetworks = new[] { "not-a-cidr-network" } } }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Get_NeverReturnsTheConfiguredSharedSecretItself()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        await client.SendAsync(
            PatchRequest(
                "/api/reverse-proxy-settings",
                new { sharedSecret = "a-genuinely-secret-value" }
            )
        );

        HttpResponseMessage response = await client.GetAsync("/api/reverse-proxy-settings");
        string body = await response.Content.ReadAsStringAsync();

        // ReverseProxyOptions is read through IOptions, and the test host never loads its settings
        // file anyway, so the GET still sees no secret at all. The assertion therefore passes
        // whether or not Get() would serialize ReverseProxyOptions.SharedSecret; it cannot catch
        // a leak.
        Assert.DoesNotContain("a-genuinely-secret-value", body);
    }

    [Fact]
    public async Task Patch_OmittingSharedSecret_LeavesTheStoredValueUntouched()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        await client.SendAsync(
            PatchRequest("/api/reverse-proxy-settings", new { sharedSecret = "the-original-value" })
        );

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/reverse-proxy-settings",
                new { sharedSecretHeaderName = "X-Renamed" }
            )
        );
        response.EnsureSuccessStatusCode();

        JsonObject root = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
        JsonObject section = (JsonObject)root["DnsServer"]["ReverseProxy"];
        Assert.Equal("the-original-value", section["SharedSecret"].GetValue<string>());
        Assert.Equal("X-Renamed", section["SharedSecretHeaderName"].GetValue<string>());
    }

    [Fact]
    public async Task Patch_WithAMalformedKnownProxyAddress_ReturnsBadRequestAndWritesNothing()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/reverse-proxy-settings",
                new { knownProxies = new[] { "not-an-ip-address" } }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAMalformedKnownNetwork_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/reverse-proxy-settings",
                new { knownNetworks = new[] { "not-a-cidr-network" } }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAnEmptyBody_ReturnsOkWithoutWritingAnything()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/reverse-proxy-settings", new { })
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Contains("nothing to change", result.Note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReverseProxySettingsEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/reverse-proxy-settings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
