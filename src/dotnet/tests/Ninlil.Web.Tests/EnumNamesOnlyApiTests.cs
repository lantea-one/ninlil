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
///     These tests pin the "member names only" rule for every API input that takes an enum as
///     text, over HTTP on the shared in-process host, signed in as the bootstrap administrator:
///     the DHCP tag-rule match modes, the recursive trust-anchor algorithm, the
///     <c>certificate.allowedProtocols</c> list of the web listener and DNS-over-TLS, the DNSCrypt
///     <c>enabledEsVersions</c> list and the query-log enum filters. A digit string, an undefined
///     number and (for the flags enum of TLS protocols) a mask or comma list must give 400, and
///     every valid name must still be accepted and stored in its canonical spelling.
///     <para>
///         The host and its settings file are shared with every other test class, so a test that
///         writes a setting writes the default back in a <c>finally</c> block and looks up what it
///         created instead of assuming empty tables.
///     </para>
/// </summary>
[Collection(WebTestCollection.Name)]
public class EnumNamesOnlyApiTests(NinlilDnsWebApplicationFactory factory)
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

    private async Task<JsonObject> ReadSettingsFileAsync() =>
        (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));

    private async Task<DhcpTagRuleDetail> CreateTagRuleAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/dhcp-tag-rules",
            new
            {
                name = $"enum-rule-{Guid.NewGuid():N}",
                priority = 5,
                vendorClassIdentifierMatch = "PXEClient",
            }
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<DhcpTagRuleDetail>();
    }

    /// <summary>A match mode given as digits or as an undefined number is refused with 400.</summary>
    [Theory]
    [InlineData("vendorClassIdentifierMatchMode", "5")]
    [InlineData("vendorClassIdentifierMatchMode", "1")]
    [InlineData("vendorClassIdentifierMatchMode", "0")]
    [InlineData("userClassMatchMode", "5")]
    [InlineData("userClassMatchMode", "1")]
    public async Task PatchTagRule_WithADigitMatchMode_ReturnsBadRequest(string field, string value)
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        DhcpTagRuleDetail rule = await CreateTagRuleAsync(client);

        try
        {
            HttpResponseMessage response = await client.SendAsync(
                PatchRequest(
                    $"/api/dhcp-tag-rules/{rule.Id}",
                    new Dictionary<string, string> { [field] = value }
                )
            );

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync();
            Assert.Contains("Exact", body);
            Assert.Contains("Prefix", body);
        }
        finally
        {
            await client.DeleteAsync($"/api/dhcp-tag-rules/{rule.Id}");
        }
    }

    /// <summary>A valid match mode name, in any case, is stored as the member.</summary>
    [Theory]
    [InlineData("Prefix", "Prefix")]
    [InlineData("prefix", "Prefix")]
    [InlineData("EXACT", "Exact")]
    public async Task PatchTagRule_WithAMatchModeName_StoresTheMember(string sent, string expected)
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        DhcpTagRuleDetail rule = await CreateTagRuleAsync(client);

        try
        {
            HttpResponseMessage response = await client.SendAsync(
                PatchRequest($"/api/dhcp-tag-rules/{rule.Id}", new { userClassMatchMode = sent })
            );
            response.EnsureSuccessStatusCode();

            DhcpTagRuleDetail after = await (
                await client.GetAsync($"/api/dhcp-tag-rules/{rule.Id}")
            ).Content.ReadFromJsonAsync<DhcpTagRuleDetail>();
            Assert.Equal(expected, after.UserClassMatchMode.ToString());
        }
        finally
        {
            await client.DeleteAsync($"/api/dhcp-tag-rules/{rule.Id}");
        }
    }

    /// <summary>A trust-anchor algorithm given as a number is refused with 400.</summary>
    [Theory]
    [InlineData("200")]
    [InlineData("8")]
    [InlineData("13")]
    public async Task PatchRecursive_WithADigitTrustAnchorAlgorithm_ReturnsBadRequest(
        string algorithm
    )
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/recursive-settings",
                new
                {
                    trustAnchorsOverride = new[]
                    {
                        new
                        {
                            keyTag = 20326,
                            algorithm,
                            digestType = 2,
                            digestHex = "AABBCC",
                        },
                    },
                }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("RsaSha256", await response.Content.ReadAsStringAsync());
    }

    /// <summary>Valid member names, in any case, are still accepted as a trust-anchor algorithm.</summary>
    [Theory]
    [InlineData("RsaSha256")]
    [InlineData("ed25519")]
    public async Task PatchRecursive_WithATrustAnchorAlgorithmName_IsAccepted(string algorithm)
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        try
        {
            HttpResponseMessage response = await client.SendAsync(
                PatchRequest(
                    "/api/recursive-settings",
                    new
                    {
                        trustAnchorsOverride = new[]
                        {
                            new
                            {
                                keyTag = 20326,
                                algorithm,
                                digestType = 2,
                                digestHex = "AABBCC",
                            },
                        },
                    }
                )
            );
            response.EnsureSuccessStatusCode();
        }
        finally
        {
            await client.SendAsync(
                PatchRequest(
                    "/api/recursive-settings",
                    new { trustAnchorsOverride = Array.Empty<object>() }
                )
            );
        }
    }

    /// <summary>
    ///     An allowed-protocols entry that is a number, a mask or a comma list is refused with 400
    ///     on both the web listener and DNS-over-TLS.
    /// </summary>
    [Theory]
    [InlineData("/api/web-listen-settings", "3072")]
    [InlineData("/api/web-listen-settings", "12288")]
    [InlineData("/api/web-listen-settings", "99999")]
    [InlineData("/api/web-listen-settings", "Tls12, Tls13")]
    [InlineData("/api/web-listen-settings", "Tls12,Tls13")]
    [InlineData("/api/transport-settings/dns-over-tls", "3072")]
    [InlineData("/api/transport-settings/dns-over-tls", "12288")]
    [InlineData("/api/transport-settings/dns-over-tls", "99999")]
    [InlineData("/api/transport-settings/dns-over-tls", "Tls12, Tls13")]
    [InlineData("/api/transport-settings/dns-over-tls", "Tls12,Tls13")]
    public async Task PatchAllowedProtocols_WithADigitMaskOrList_ReturnsBadRequest(
        string url,
        string entry
    )
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(url, new { certificate = new { allowedProtocols = new[] { entry } } })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Tls12", await response.Content.ReadAsStringAsync());
    }

    /// <summary>A valid protocol name in any case is written as the canonical name.</summary>
    [Theory]
    [InlineData("tls12", "Tls12")]
    [InlineData("TLS13", "Tls13")]
    public async Task PatchAllowedProtocols_WithAName_WritesTheCanonicalName(
        string sent,
        string expected
    )
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        try
        {
            HttpResponseMessage webResponse = await client.SendAsync(
                PatchRequest(
                    "/api/web-listen-settings",
                    new { certificate = new { allowedProtocols = new[] { sent } } }
                )
            );
            webResponse.EnsureSuccessStatusCode();
            HttpResponseMessage tlsResponse = await client.SendAsync(
                PatchRequest(
                    "/api/transport-settings/dns-over-tls",
                    new { certificate = new { allowedProtocols = new[] { sent } } }
                )
            );
            tlsResponse.EnsureSuccessStatusCode();

            JsonObject root = await ReadSettingsFileAsync();
            Assert.Equal(
                expected,
                root["DnsServer"]
                    ["Protocol"]["DnsOverTls"]["Certificate"]["AllowedProtocols"]
                    .GetValue<string>()
            );
            Assert.Equal(
                expected,
                root["DnsServer"]["Web"]["Certificate"]["AllowedProtocols"].GetValue<string>()
            );
        }
        finally
        {
            await client.SendAsync(
                PatchRequest(
                    "/api/web-listen-settings",
                    new { certificate = new { allowedProtocols = Array.Empty<string>() } }
                )
            );
            await client.SendAsync(
                PatchRequest(
                    "/api/transport-settings/dns-over-tls",
                    new { certificate = new { allowedProtocols = Array.Empty<string>() } }
                )
            );
        }
    }

    /// <summary>A DNSCrypt cipher given as a number is refused with 400.</summary>
    [Theory]
    [InlineData("99")]
    [InlineData("1")]
    [InlineData("2")]
    public async Task PatchDnsCrypt_WithADigitEsVersion_ReturnsBadRequest(string entry)
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/transport-settings/dnscrypt",
                new { enabledEsVersions = new[] { entry } }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("XChaCha20Poly1305", await response.Content.ReadAsStringAsync());
    }

    /// <summary>A DNSCrypt cipher name in any case is written in its canonical spelling.</summary>
    [Fact]
    public async Task PatchDnsCrypt_WithAnEsVersionName_WritesTheCanonicalName()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        JsonObject before = await ReadSettingsFileAsync();
        JsonNode original = before["DnsServer"]?["Protocol"]?["DnsCrypt"]?["EnabledEsVersions"];

        try
        {
            HttpResponseMessage response = await client.SendAsync(
                PatchRequest(
                    "/api/transport-settings/dnscrypt",
                    new { enabledEsVersions = new[] { "xchacha20poly1305" } }
                )
            );
            response.EnsureSuccessStatusCode();

            JsonObject root = await ReadSettingsFileAsync();
            JsonArray written = (JsonArray)
                root["DnsServer"]["Protocol"]["DnsCrypt"]["EnabledEsVersions"];
            Assert.Equal(["XChaCha20Poly1305"], written.Select(item => item.GetValue<string>()));
        }
        finally
        {
            string[] restore = original is JsonArray array
                ? [.. array.Select(item => item.GetValue<string>())]
                : ["XChaCha20Poly1305", "XSalsa20Poly1305"];
            await client.SendAsync(
                PatchRequest(
                    "/api/transport-settings/dnscrypt",
                    new { enabledEsVersions = restore }
                )
            );
        }
    }

    /// <summary>A query-log enum filter given as a number is refused with 400.</summary>
    [Theory]
    [InlineData("queriedType=99")]
    [InlineData("queriedType=1")]
    [InlineData("responseCode=99")]
    [InlineData("responseCode=0")]
    [InlineData("clientProtocolVersion=99")]
    public async Task SearchQueryLog_WithADigitEnumFilter_ReturnsBadRequest(string query)
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync($"/api/query-log?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>The [EnumMember] wire name still works as a query-log filter.</summary>
    [Theory]
    [InlineData("responseCode=NXDOMAIN")]
    [InlineData("responseCode=nxdomain")]
    [InlineData("responseCode=NxDomain")]
    public async Task SearchQueryLog_WithAWireNameOrMemberName_Succeeds(string query)
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.GetAsync($"/api/query-log?{query}");

        response.EnsureSuccessStatusCode();
    }
}
