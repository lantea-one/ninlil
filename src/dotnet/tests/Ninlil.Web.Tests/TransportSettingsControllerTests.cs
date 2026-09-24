using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="TransportSettingsController" /> end to end, against a real,
///     in-process host and an isolated, throwaway JSON configuration file (see
///     <see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />).
///     <para>
///         <see cref="DnsOverHttps_TogglingEnable_GenuinelyStartsAndStopsAnsweringRealRequests" />
///         goes further than a plain read/write round trip, proving this controller's own central
///         claim -- that an <c>enable</c> toggle is genuinely live, with no restart -- against real
///         transport behavior on the very same running host: it sends real GET requests at
///         <c>/dns-query</c> through this host's own shared Kestrel pipeline (the exact same route
///         <c>DnsOverHttpsRequestHandler</c> answers real traffic on) before and after each toggle,
///         proving the structural fix in <c>EndpointRouteBuilderDnsOverHttpsExtension</c> (routes
///         always mapped, gated by <c>DnsOverHttpsTransportGate</c> alone) actually works for an
///         installation that started with this transport off, this project's own default. This is
///         the one transport whose live-toggle proof this shared collection's own
///         <see cref="NinlilDnsWebApplicationFactory" /> instance can actually observe --
///         <c>DnsOverHttpsTransportGate</c>'s own start/stop is a plain in-memory flag flip, not
///         driven by re-reading configuration, unlike every other transport's own real listener.
///         <see cref="PatchDnsCrypt_EnablingWithAProviderNameAndPort_WritesEveryFieldAndReportsTheMixedNote" />'s
///         own doc comment explains why DNSCrypt/DNS-over-TLS/DNS-over-QUIC's own equally-real live
///         toggles are proven a different way instead -- a committed listener-level hot-reload test
///         plus a throwaway smoke test against a real, standalone compiled host, not this class.
///     </para>
///     <para>
///         The DNS-over-HTTPS live-toggle test restores the transport to disabled in a
///         <c>finally</c> block -- this class shares one real host with every other test class in
///         <see cref="WebTestCollection" />, so a live toggle left on would otherwise leak into an
///         unrelated later test.
///     </para>
/// </summary>
[Collection(WebTestCollection.Name)]
public class TransportSettingsControllerTests(NinlilDnsWebApplicationFactory factory)
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
    public async Task GetPlain_ReturnsTheCompiledInDefaultsWhenNothingHasBeenConfigured()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync("/api/transport-settings/plain");
        response.EnsureSuccessStatusCode();

        PlainTransportSettingsDetail detail =
            await response.Content.ReadFromJsonAsync<PlainTransportSettingsDetail>();

        // This shared test host overrides Plain DNS's own port to a random high one (see
        // NinlilDnsWebApplicationFactory's own doc comment -- binding the real, compiled-in
        // default of 53 would need root), so that field is checked against the actual override
        // rather than this class's own compiled-in default; every other field is untouched by
        // that override and still reflects the real default.
        Assert.Equal(factory.PlainDnsPort, detail.Port);
        Assert.Equal(TimeSpan.FromSeconds(30), detail.TcpIdleTimeout);
        Assert.Equal(512, detail.MaxConcurrentTcpConnections);
    }

    [Fact]
    public async Task GetDnsOverTls_ReturnsTheCompiledInDefaultsWhenNothingHasBeenConfigured()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync(
            "/api/transport-settings/dns-over-tls"
        );
        response.EnsureSuccessStatusCode();

        DnsOverTlsSettingsDetail detail =
            await response.Content.ReadFromJsonAsync<DnsOverTlsSettingsDetail>();

        Assert.False(detail.Enable);
        Assert.Equal(853, detail.Port);
        Assert.False(detail.Certificate.PfxPasswordConfigured);
        Assert.Empty(detail.Certificate.AllowedProtocols);
    }

    [Fact]
    public async Task GetDnsOverQuic_ReturnsTheCompiledInDefaultsWhenNothingHasBeenConfigured()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync(
            "/api/transport-settings/dns-over-quic"
        );
        response.EnsureSuccessStatusCode();

        DnsOverQuicSettingsDetail detail =
            await response.Content.ReadFromJsonAsync<DnsOverQuicSettingsDetail>();

        Assert.False(detail.Enable);
        Assert.Equal(853, detail.Port);
        Assert.Equal(100, detail.MaxConcurrentBidirectionalStreams);
    }

    [Fact]
    public async Task GetDnsOverHttps_ReturnsTheCompiledInDefaultsWhenNothingHasBeenConfigured()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync(
            "/api/transport-settings/dns-over-https"
        );
        response.EnsureSuccessStatusCode();

        DnsOverHttpsSettingsDetail detail =
            await response.Content.ReadFromJsonAsync<DnsOverHttpsSettingsDetail>();

        Assert.False(detail.Enable);
        Assert.Equal("/dns-query", detail.Path);
        Assert.Equal(65535, detail.MaxMessageSizeBytes);
    }

    [Fact]
    public async Task GetDnsCrypt_ReturnsTheCompiledInDefaultsWhenNothingHasBeenConfigured()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync("/api/transport-settings/dnscrypt");
        response.EnsureSuccessStatusCode();

        DnsCryptSettingsDetail detail =
            await response.Content.ReadFromJsonAsync<DnsCryptSettingsDetail>();

        Assert.False(detail.Enable);
        Assert.Equal(5353, detail.Port);
        Assert.Equal(2, detail.EnabledEsVersions.Count);
    }

    [Fact]
    public async Task PatchPlain_WithAPortChange_WritesItAndReportsRestartRequired()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/transport-settings/plain", new { maxConcurrentTcpConnections = 256 })
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Equal(RuntimeSettingsWriteResult.TransportRestartRequiredNote, result.Note);

        JsonObject root = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
        JsonObject section = (JsonObject)root["DnsServer"]["Protocol"]["Plain"];
        Assert.Equal(256, section["MaxConcurrentTcpConnections"].GetValue<int>());
    }

    [Fact]
    public async Task PatchDnsOverTls_WithAnInvalidIdleTimeout_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/transport-settings/dns-over-tls",
                new { idleTimeout = "not-a-timespan" }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchDnsOverTls_WithAnInvalidAllowedProtocol_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/transport-settings/dns-over-tls",
                new { certificate = new { allowedProtocols = new[] { "NotARealProtocol" } } }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchDnsOverTls_WithARealAllowedProtocolsList_WritesAJoinedFlagsString()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/transport-settings/dns-over-tls",
                new { certificate = new { allowedProtocols = new[] { "Tls12", "Tls13" } } }
            )
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Equal(RuntimeSettingsWriteResult.TransportRestartRequiredNote, result.Note);

        JsonObject root = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
        string allowedProtocols = root["DnsServer"]
            ["Protocol"]["DnsOverTls"]["Certificate"]["AllowedProtocols"]
            .GetValue<string>();
        Assert.Contains("Tls12", allowedProtocols);
        Assert.Contains("Tls13", allowedProtocols);

        // Deliberately does not also GET this setting back and assert the new value: this shared
        // collection's own NinlilDnsWebApplicationFactory instance points every runtime
        // settings write at an isolated file that is not one of this host's own watched
        // IConfiguration sources (see its own doc comment), so IOptionsMonitor<DnsOverTlsOptions>
        // never actually observes a write made this way inside this specific test process --
        // exactly the same reason every other runtime settings controller's own test class in
        // this project only ever asserts against the file's own content after a PATCH, never a
        // live re-read. TransportSettingsHotReloadTests (a sibling file in this same project)
        // proves the underlying IOptionsMonitor round trip directly for DNSCrypt, with no such
        // isolation in the way.
    }

    [Fact]
    public async Task PatchDnsCrypt_WithAnInvalidEsVersion_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/transport-settings/dnscrypt",
                new { enabledEsVersions = new[] { "NotARealCipher" } }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchPlain_WithAnEmptyBody_ReturnsOkWithoutWritingAnything()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/transport-settings/plain", new { })
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Contains("nothing to change", result.Note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TransportSettingsEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/transport-settings/plain");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    ///     This method builds a minimal, valid A-record query for <paramref name="name" />.
    /// </summary>
    private static DnsMessage BuildQuery(ushort id, string name) =>
        new()
        {
            Id = id,
            Opcode = DnsOpcodeEnum.Query,
            RecursionDesired = true,
            Questions =
            {
                new DnsQuestion(
                    DnsName.Parse(name),
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet
                ),
            },
        };

    [Fact]
    public async Task DnsOverHttps_TogglingEnable_GenuinelyStartsAndStopsAnsweringRealRequests()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        byte[] queryBytes = DnsMessageWriter.Write(
            BuildQuery(1, "transport-settings-doh-test.invalid.")
        );
        string encodedQuery = Base64Url.EncodeToString(queryBytes);
        string queryUrl = $"/dns-query?dns={encodedQuery}";

        try
        {
            // Established as a known starting point, deliberately, rather than assumed from this
            // transport's own compiled-in default -- another test earlier in this shared
            // collection could conceivably have left it either way.
            HttpResponseMessage disablePatch = await client.SendAsync(
                PatchRequest("/api/transport-settings/dns-over-https", new { enable = false })
            );
            disablePatch.EnsureSuccessStatusCode();

            HttpResponseMessage beforeResponse = await client.GetAsync(queryUrl);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, beforeResponse.StatusCode);

            HttpResponseMessage enablePatch = await client.SendAsync(
                PatchRequest("/api/transport-settings/dns-over-https", new { enable = true })
            );
            enablePatch.EnsureSuccessStatusCode();

            RuntimeSettingsWriteResult enableResult =
                await enablePatch.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
            Assert.Equal(RuntimeSettingsWriteResult.TransportEnableToggledNote, enableResult.Note);

            // Genuinely live: the identical already-running host, with no restart, now answers a
            // real DNS-over-HTTPS request at the exact route DnsOverHttpsRequestHandler always
            // maps -- proof that EndpointRouteBuilderDnsOverHttpsExtension's own "always map,
            // gate the answer" fix actually closes the gap an installation that started with this
            // transport off (this project's own default) used to hit.
            HttpResponseMessage afterResponse = await client.GetAsync(queryUrl);
            Assert.Equal(HttpStatusCode.OK, afterResponse.StatusCode);

            byte[] responseBytes = await afterResponse.Content.ReadAsByteArrayAsync();
            DnsMessage responseMessage = DnsMessageReader.Read(responseBytes);
            Assert.True(responseMessage.IsResponse);
            Assert.Equal(1, responseMessage.Id);

            HttpResponseMessage disableAgainPatch = await client.SendAsync(
                PatchRequest("/api/transport-settings/dns-over-https", new { enable = false })
            );
            disableAgainPatch.EnsureSuccessStatusCode();

            HttpResponseMessage afterDisableResponse = await client.GetAsync(queryUrl);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, afterDisableResponse.StatusCode);
        }
        finally
        {
            await client.SendAsync(
                PatchRequest("/api/transport-settings/dns-over-https", new { enable = false })
            );
        }
    }

    [Fact]
    public async Task PatchDnsCrypt_EnablingWithAProviderNameAndPort_WritesEveryFieldAndReportsTheMixedNote()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        try
        {
            HttpResponseMessage response = await client.SendAsync(
                PatchRequest(
                    "/api/transport-settings/dnscrypt",
                    new
                    {
                        enable = true,
                        port = 55353,
                        providerName = "transport-settings-test.invalid.",
                    }
                )
            );
            response.EnsureSuccessStatusCode();

            // enable is genuinely live (this controller starts/stops the real dnscrypt-udp/
            // dnscrypt-tcp/dnscrypt-certificate-rotation registrations to match, see this
            // controller's own doc comment) -- but observing that live effect from outside the
            // process needs a real, standalone compiled host, not this shared collection's own
            // WebApplicationFactory instance, whose own isolated runtime-settings file
            // (NinlilDnsWebApplicationFactory's own doc comment) is deliberately not one of
            // this host's own watched IConfiguration sources, so IOptionsMonitor<DnsCryptOptions>
            // never actually observes a write made this way inside this specific test process.
            // DnsOverHttpsTransportGate has no such dependency (its own start/stop is a plain
            // in-memory flag, not driven by re-reading configuration at all), which is exactly
            // why DnsOverHttps_TogglingEnable_GenuinelyStartsAndStopsAnsweringRealRequests above
            // is this suite's own real, externally-observable live-toggle proof; a real socket-
            // level DNSCrypt proof is instead a throwaway smoke test against the actual compiled
            // Ninlil.Web.dll, per this project's own smoke-test convention.
            RuntimeSettingsWriteResult result =
                await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
            Assert.Equal(RuntimeSettingsWriteResult.TransportMixedReloadNote, result.Note);

            JsonObject root = (JsonObject)
                JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
            JsonObject section = (JsonObject)root["DnsServer"]["Protocol"]["DnsCrypt"];
            Assert.True(section["Enable"].GetValue<bool>());
            Assert.Equal(55353, section["Port"].GetValue<int>());
            Assert.Equal(
                "transport-settings-test.invalid.",
                section["ProviderName"].GetValue<string>()
            );
        }
        finally
        {
            await client.SendAsync(
                PatchRequest("/api/transport-settings/dnscrypt", new { enable = false })
            );
        }
    }
}
