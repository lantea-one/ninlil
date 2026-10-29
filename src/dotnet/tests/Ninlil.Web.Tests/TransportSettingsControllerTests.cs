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
///     This class covers <see cref="TransportSettingsController" /> over HTTP on the shared
///     in-process host, signed in as the bootstrap administrator: the compiled-in defaults of each
///     transport, patches checked by reading the host's temporary settings file
///     (<see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />) and the
///     returned note, listen-address normalization and validation, and other input validation.
///     <para>
///         <see cref="DnsOverHttps_TogglingEnable_GenuinelyStartsAndStopsAnsweringRealRequests" />
///         also checks that the <c>enable</c> switch takes effect without a restart, by sending
///         DNS-over-HTTPS GET requests to <c>/dns-query</c> on the same host before and after each
///         toggle. The route is always mapped and only gated by <c>DnsOverHttpsTransportGate</c>
///         (see <c>EndpointRouteBuilderDnsOverHttpsExtension</c>), so an installation that
///         starts with the transport off, the default, can turn it on live. DNS-over-HTTPS is the
///         one transport whose toggle this host can observe, because its gate is an in-memory
///         flag rather than a listener that reads its options; for DNSCrypt see
///         <see cref="TransportSettingsHotReloadTests" />. DNS-over-TLS and DNS-over-QUIC toggles
///         are not checked here.
///     </para>
///     <para>
///         Tests that turn a transport on turn it off again in a <c>finally</c> block, since the
///         host is shared with every other test class.
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

        // The test host moves Plain DNS to a free high port (port 53 needs root; see
        // NinlilDnsWebApplicationFactory), so the port is checked against that override. The
        // other fields keep their compiled-in defaults.
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
        Assert.Equal(512, detail.MaxConcurrentConnections);
    }

    [Fact]
    public async Task PatchDnsOverQuic_WithZeroMaxConcurrentConnections_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/transport-settings/dns-over-quic",
                new { maxConcurrentConnections = 0 }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PatchDnsOverQuic_WithMaxConcurrentConnections_WritesItAndReportsRestartRequired()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/transport-settings/dns-over-quic",
                new { maxConcurrentConnections = 64 }
            )
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Equal(RuntimeSettingsWriteResult.TransportRestartRequiredNote, result.Note);

        JsonObject root = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
        Assert.Equal(
            64,
            root["DnsServer"]["Protocol"]["DnsOverQuic"]["MaxConcurrentConnections"].GetValue<int>()
        );
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
        // The test host moves DNSCrypt to a free port too (see NinlilDnsWebApplicationFactory).
        Assert.Equal(factory.DnsCryptPort, detail.Port);
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

        // Only the file is checked; a GET would still show the old values (see
        // NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath).
        // TransportSettingsHotReloadTests covers the IOptionsMonitor round trip for DNSCrypt.
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
    public async Task PatchDnsCrypt_WithAnUnparseableProviderName_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/transport-settings/dnscrypt", new { providerName = "a..b" })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("resolver.example")]
    [InlineData("2.dnscrypt-cert.resolver.example")]
    [InlineData(null)]
    public async Task PatchDnsCrypt_WithAUsableOrNullProviderName_ReturnsOk(string providerName)
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/transport-settings/dnscrypt", new { providerName })
        );

        response.EnsureSuccessStatusCode();
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
            // Set a known starting point rather than assuming the default, since an earlier test
            // could have left the transport on.
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

            // Without a restart, the same host now answers a DNS-over-HTTPS request on the route
            // that is always mapped and only gated (see the class summary).
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

    [Theory]
    [InlineData("path")]
    [InlineData("maxMessageSizeBytes")]
    public async Task PatchDnsOverHttps_WithAFieldReadOnceAtRouteMapping_ReportsTheProcessRestartNote(
        string field
    )
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        // The values equal the defaults; the shared host does not read the runtime file back, so
        // the write changes nothing live.
        object body =
            field == "path" ? new { path = "/dns-query" } : new { maxMessageSizeBytes = 65535 };

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/transport-settings/dns-over-https", body)
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Equal(RuntimeSettingsWriteResult.DnsOverHttpsRestartRequiredNote, result.Note);
    }

    [Fact]
    public async Task PatchDnsOverHttps_WithEnableAndPath_ReportsTheMixedNote()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        try
        {
            HttpResponseMessage response = await client.SendAsync(
                PatchRequest(
                    "/api/transport-settings/dns-over-https",
                    new { enable = false, path = "/dns-query" }
                )
            );
            response.EnsureSuccessStatusCode();

            RuntimeSettingsWriteResult result =
                await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
            Assert.Equal(RuntimeSettingsWriteResult.DnsOverHttpsMixedReloadNote, result.Note);
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

            // Enabling starts the dnscrypt-udp, dnscrypt-tcp and dnscrypt-certificate-rotation
            // services (see TransportSettingsController). The host's DNSCrypt options never see
            // the written port or provider name (see
            // NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath), so the listeners use
            // the startup values, and the test checks only the note and the file, not whether
            // DNSCrypt answers.
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

    [Theory]
    [InlineData("plain", "Plain")]
    [InlineData("dns-over-tls", "DnsOverTls")]
    [InlineData("dns-over-quic", "DnsOverQuic")]
    [InlineData("dnscrypt", "DnsCrypt")]
    public async Task PatchListenAddresses_WritesTheNormalizedCommaSeparatedList(
        string transportPath,
        string sectionName
    )
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        string url = $"/api/transport-settings/{transportPath}";

        try
        {
            HttpResponseMessage response = await client.SendAsync(
                PatchRequest(
                    url,
                    new { listenAddresses = new[] { " 127.0.0.1", "::1", "127.0.0.1" } }
                )
            );
            response.EnsureSuccessStatusCode();

            // Trimmed and de-duplicated, and stored as one string so an environment variable can
            // hold it too.
            Assert.Equal("127.0.0.1, ::1", ReadListenAddresses(sectionName));
        }
        finally
        {
            // This host is shared with every other test class; put the default back.
            (
                await client.SendAsync(
                    PatchRequest(url, new { listenAddresses = Array.Empty<string>() })
                )
            ).EnsureSuccessStatusCode();
        }

        Assert.Equal("", ReadListenAddresses(sectionName));
    }

    [Theory]
    [InlineData("not-an-address", "is not a valid IP address")]
    [InlineData("127.1", "is not a valid IP address")]
    [InlineData("[::1]:53", "is not a valid IP address")]
    [InlineData("010.0.0.1", "is not a valid IP address")]
    public async Task PatchListenAddresses_WithAMalformedAddress_ReturnsBadRequestNamingIt(
        string entry,
        string expectedMessage
    )
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/transport-settings/dns-over-tls",
                new { listenAddresses = new[] { "127.0.0.1", entry } }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains(expectedMessage, body);
        Assert.Contains(entry, body);
    }

    [Fact]
    public async Task PatchListenAddresses_WithAnAddressAWildcardAlreadyCovers_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/transport-settings/plain",
                new { listenAddresses = new[] { "0.0.0.0", "192.168.1.1" } }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("already covered by", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PatchListenAddresses_ThatIsNotAnArrayOfStrings_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/transport-settings/plain", new { listenAddresses = "127.0.0.1" })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("must be a JSON array", await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    ///     This method reads one transport's stored <c>ListenAddresses</c> from this host's
    ///     configuration file, or an empty string when it isn't set.
    /// </summary>
    private string ReadListenAddresses(string sectionName)
    {
        JsonObject root = (JsonObject)
            JsonNode.Parse(File.ReadAllText(factory.RuntimeConfigurationFilePath));
        return root["DnsServer"]?["Protocol"]?[sectionName]?["ListenAddresses"]?.GetValue<string>()
            ?? string.Empty;
    }
}
