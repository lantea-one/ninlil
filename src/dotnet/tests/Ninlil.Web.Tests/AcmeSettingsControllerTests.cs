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
///     This class covers the <c>Get</c> and <c>Patch</c> actions of
///     <see cref="AcmeSettingsController" /> (ACME and CAA settings) over HTTP on the shared
///     in-process host, signed in as the bootstrap administrator. A patch is checked by reading
///     the host's temporary settings file
///     (<see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />) and the
///     returned reload note, not by a follow-up GET: that file is not a configuration source in
///     the test host, so <c>Get</c> always shows the compiled-in defaults.
///     <see cref="AcmeOptionsHotReloadTests" /> covers the hot reload itself.
///     <para>
///         The certificate inventory and the issue-or-renew trigger against a configured domain
///         are covered by <see cref="AcmeSettingsControllerDirectTests" />, which also explains
///         why a full round trip with a certificate authority is not tested.
///     </para>
/// </summary>
[Collection(WebTestCollection.Name)]
public class AcmeSettingsControllerTests(NinlilDnsWebApplicationFactory factory)
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

        HttpResponseMessage response = await client.GetAsync("/api/acme-settings");
        response.EnsureSuccessStatusCode();

        AcmeSettingsDetail detail = await response.Content.ReadFromJsonAsync<AcmeSettingsDetail>();

        // ACME defaults off. CAA auto-management defaults on because it writes nothing until an
        // issuer domain is set (see CaaAutoManagementOptions.DefaultEnabled).
        Assert.False(detail.Enable);
        Assert.Equal(
            new Uri("https://acme-v02.api.letsencrypt.org/directory"),
            detail.DirectoryUri
        );
        Assert.Equal("Ninlil", detail.CsrOrganization);
        Assert.True(detail.CaaAutoManagement.DefaultEnabled);
    }

    [Fact]
    public async Task Patch_WithNewScalarValues_WritesThemToTheConfigurationFileAndReportsHotReload()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/acme-settings",
                new
                {
                    enable = true,
                    contactEmails = new[] { "admin@acme-settings-tests.invalid" },
                    domains = new[] { "acme-scalar-patch.acme-settings-tests.invalid" },
                    renewBeforeExpiration = "20.00:00:00",
                    pollInterval = "06:00:00",
                    csrOrganization = "Acme Settings Tests",
                }
            )
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Equal(RuntimeSettingsWriteResult.HotReloadedNote, result.Note);

        JsonObject root = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
        JsonObject section = (JsonObject)root["DnsServer"]["Acme"];
        Assert.True(section["Enable"].GetValue<bool>());
        Assert.Equal("20.00:00:00", section["RenewBeforeExpiration"].GetValue<string>());
        Assert.Equal("Acme Settings Tests", section["CsrOrganization"].GetValue<string>());
    }

    [Fact]
    public async Task Patch_WithCaaAutoManagement_WritesTheNestedSectionAndReportsHotReload()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/acme-settings",
                new
                {
                    caaAutoManagement = new
                    {
                        defaultEnabled = false,
                        defaultIssuerDomain = "letsencrypt.org",
                    },
                }
            )
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Equal(RuntimeSettingsWriteResult.HotReloadedNote, result.Note);

        JsonObject root = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
        JsonObject section = (JsonObject)root["DnsServer"]["Acme"]["CaaAutoManagement"];
        Assert.False(section["DefaultEnabled"].GetValue<bool>());
        Assert.Equal("letsencrypt.org", section["DefaultIssuerDomain"].GetValue<string>());

        // Put the default back in the shared settings file.
        await client.SendAsync(
            PatchRequest(
                "/api/acme-settings",
                new { caaAutoManagement = new { defaultEnabled = true } }
            )
        );
    }

    [Fact]
    public async Task Patch_WithAnAccountStateFilePath_ReportsRestartRequired()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/acme-settings",
                new { accountStateFilePath = "/tmp/acme-settings-tests-account.json" }
            )
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Equal(RuntimeSettingsWriteResult.RestartRequiredNote, result.Note);
    }

    [Fact]
    public async Task Patch_MixingAHotReloadingFieldAndTheRestartRequiredField_ReportsTheMixedNote()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/acme-settings",
                new
                {
                    enable = true,
                    accountStateFilePath = "/tmp/acme-settings-tests-account-mixed.json",
                }
            )
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Equal(RuntimeSettingsWriteResult.MixedReloadNote, result.Note);
    }

    [Fact]
    public async Task Patch_WithAnEmptyBody_ReturnsOkWithoutWritingAnything()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/acme-settings", new { })
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Contains("nothing to change", result.Note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Patch_WithAnInvalidDirectoryUri_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/acme-settings", new { directoryUri = "not-a-url" })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAnInvalidDomain_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        // DnsName.TryParse accepts most printable labels (RFC 1035's letters, digits and hyphens
        // rule is advisory), so an invalid name has to break a hard limit, such as the 63-octet
        // maximum label length.
        string labelOverMaximumLength = new string('a', 64);

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/acme-settings",
                new { domains = new[] { $"{labelOverMaximumLength}.acme-settings-tests.invalid" } }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("exa mple.com")]
    [InlineData("_x.example.com")]
    [InlineData("*.*.example.com")]
    [InlineData("com")]
    public async Task Patch_WithADomainThatIsNotAHostName_ReturnsBadRequest(string domain)
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/acme-settings", new { domains = new[] { domain } })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAWildcardDomainAndItsBaseName_IsAccepted()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/acme-settings",
                new
                {
                    domains = new[]
                    {
                        "*.acme-wildcard.acme-settings-tests.invalid",
                        "acme-wildcard.acme-settings-tests.invalid",
                    },
                }
            )
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAnInvalidContactEmail_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/acme-settings", new { contactEmails = new[] { "not-an-email" } })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithANonPositiveRenewBeforeExpiration_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/acme-settings", new { renewBeforeExpiration = "00:00:00" })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AcmeSettingsEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/acme-settings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task IssueOrRenewNow_AgainstAnUnconfiguredDomain_ReturnsNotFound()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsync(
            "/api/acme-settings/certificates/not-a-configured-domain.acme-settings-tests.invalid/issue",
            null
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
