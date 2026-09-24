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
///     This class covers <see cref="AcmeSettingsController" />'s <c>Get</c>/<c>Patch</c> actions
///     (ACME/CAA settings) end to end, against a real, in-process host and an isolated, throwaway
///     JSON configuration file (see
///     <see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />) -- closing
///     a real "LE Certs" configuration gap: before this
///     controller existed, ACME/CAA settings had no REST/UI surface at all.
///     <para>
///         <b>What this class does not cover, and why:</b> <c>Certificates</c>/<c>IssueOrRenewNow</c>
///         (the real per-domain certificate inventory) live in
///         <c>AcmeSettingsControllerDirectTests</c> instead -- see that class's own doc comment.
///         A real end-to-end order/challenge/finalize round trip against Let's Encrypt (or any
///         other real CA) needs a real, publicly-resolvable domain this environment does not have,
///         a documented, known verification boundary.
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

        // The exact, documented, security-conscious defaults -- see AcmeOptions.Enable's own doc
        // comment for why ACME itself defaults off while global CAA auto-management (an
        // improvement in service of an already-explicit ACME opt-in, not a new capability of its
        // own) defaults on.
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

        // This class deliberately does not also confirm the write via a follow-up GET the way
        // its own doc comment might suggest -- this shared WebApplicationFactory host's runtime
        // configuration file is overridden to an isolated path via an environment variable, which
        // Program.cs never separately registers as its own reload-on-change IConfiguration source
        // (only the default appsettings.json gets that treatment automatically); a real
        // deployment's own default path IS auto-watched, so genuinely live IOptionsMonitor
        // hot-reload is proved instead by a dedicated, lower-level test that builds its own real
        // IConfigurationRoot directly -- see AcmeOptionsHotReloadTests -- matching
        // RecursiveSettingsControllerTests/AppearanceSettingsControllerTests's own identical
        // precedent for their own global (non-per-user) settings.
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

        // See Patch_WithNewScalarValues_...'s own comment for why this class checks only the raw
        // file here, not a follow-up GET -- genuine IOptionsMonitor hot-reload is proved by
        // AcmeOptionsHotReloadTests instead.

        // Restore the default so a later test in this same shared collection sees the compiled-in
        // default again.
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

        // DnsName.TryParse's real wire-format validation accepts most printable-character labels
        // (RFC 1035's "preferred name syntax" restriction to letters/digits/hyphens is advisory,
        // not enforced at parse time) -- a genuinely invalid value needs to violate an actual
        // hard constraint DnsName.TryParse enforces, such as a label over its own 63-octet
        // MaximumLabelLength.
        string labelOverMaximumLength = new string('a', 64);

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/acme-settings",
                new { domains = new[] { $"{labelOverMaximumLength}.acme-settings-tests.invalid" } }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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

    // Certificates()'s own real per-domain inventory behavior, and IssueOrRenewNow()'s own real
    // failure-recording behavior against a domain this host's AcmeOptions.Domains genuinely
    // names, are covered by AcmeSettingsControllerDirectTests instead of here -- both need a
    // specific, already-live AcmeOptions value (a set of configured domains; an unreachable
    // directoryUri) that this shared WebApplicationFactory host cannot deliver through a real
    // PATCH+GET round trip (see Patch_WithNewScalarValues_...'s own comment above), so that class
    // constructs AcmeSettingsController directly instead, against a fixed, already-correct
    // IOptionsMonitor<AcmeOptions> -- no reload dependency, no timing race.
}
