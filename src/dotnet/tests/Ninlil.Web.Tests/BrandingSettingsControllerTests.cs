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
///     This class covers <see cref="BrandingSettingsController" /> end to end, against a real,
///     in-process host and an isolated, throwaway JSON configuration file (see
///     <see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />) -- covers
///     <c>BrandingOptions</c> (site title/logo) alongside the already-existing
///     <c>AuthoritativeServerOptions</c> CHAOS-query fields, both merged through the same
///     <c>PATCH</c> action. Genuine <c>IOptionsMonitor</c> hot-reload for the CHAOS-query fields
///     is proved separately, end to end against the real .NET configuration/options binding
///     pipeline, by <c>AuthoritativeQueryResolverChaosHotReloadTests</c> (in
///     <c>Ninlil.Dns.Server.Authoritative.Tests</c>) -- see
///     <see cref="TsigSettingsControllerTests" />'s own doc comment for why that proof does not
///     run through this shared HTTP fixture instead.
/// </summary>
[Collection(WebTestCollection.Name)]
public class BrandingSettingsControllerTests(NinlilDnsWebApplicationFactory factory)
{
    /// <summary>
    ///     This constant contains a tiny (1x1 transparent PNG), genuinely valid, well-formed data
    ///     URI -- real enough to exercise <see cref="Ninlil.Common.Serializer.Model.DataUriModel" />'s
    ///     own parsing rather than a synthetic string this project's own validation would accept
    ///     for the wrong reason.
    /// </summary>
    private const string ValidPngLogoDataUri =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";

    private HttpClient CreateClient() => factory.CreateClient();

    private async Task<HttpClient> CreateAuthenticatedAdminClientAsync()
    {
        HttpClient client = CreateClient();

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

    /// <summary>
    ///     This method creates a fresh, dedicated, ordinary (non-administrative, no permissions of
    ///     its own) user account and returns a client already authenticated as it -- mirrors
    ///     <see cref="AppearanceSettingsControllerTests.CreateOrdinaryUserClientAsync" />'s own
    ///     setup shape, used here to prove <see cref="BrandingSettingsController.GetEffective" />
    ///     needs no specific permission at all, unlike <see cref="BrandingSettingsController.Get" />.
    /// </summary>
    private async Task<HttpClient> CreateOrdinaryUserClientAsync(string userNameSuffix)
    {
        HttpClient adminClient = await CreateAuthenticatedAdminClientAsync();

        string userName = $"branding-test-user-{userNameSuffix}";
        const string password = "Branding-Test-Password-1234!";

        HttpResponseMessage createResponse = await adminClient.PostAsJsonAsync(
            "/api/users",
            new CreateUserRequest(
                userName,
                password,
                $"{userName}@localhost.local",
                "Branding Test User"
            )
        );
        createResponse.EnsureSuccessStatusCode();

        HttpClient userClient = CreateClient();
        HttpResponseMessage userLoginResponse = await userClient.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(userName, password)
        );
        userLoginResponse.EnsureSuccessStatusCode();
        LoginResponse userLogin =
            await userLoginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        userClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            userLogin.AccessToken
        );

        return userClient;
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
        HttpClient client = await CreateAuthenticatedAdminClientAsync();

        HttpResponseMessage response = await client.GetAsync("/api/branding-settings");
        response.EnsureSuccessStatusCode();

        BrandingSettingsDetail detail =
            await response.Content.ReadFromJsonAsync<BrandingSettingsDetail>();

        Assert.Null(detail.SiteTitle);
        Assert.Null(detail.LogoDataUri);
        Assert.False(detail.EnableChaosVersionQueries);
        Assert.Equal("Ninlil", detail.VersionString);
    }

    [Fact]
    public async Task Patch_WithASiteTitleAndLogo_WritesBothToTheBrandingSection()
    {
        HttpClient client = await CreateAuthenticatedAdminClientAsync();

        try
        {
            HttpResponseMessage response = await client.SendAsync(
                PatchRequest(
                    "/api/branding-settings",
                    new { siteTitle = "Acme Corp DNS", logoDataUri = ValidPngLogoDataUri }
                )
            );
            response.EnsureSuccessStatusCode();

            RuntimeSettingsWriteResult result =
                await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
            Assert.Equal(RuntimeSettingsWriteResult.HotReloadedNote, result.Note);

            JsonObject root = (JsonObject)
                JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
            JsonObject section = (JsonObject)root["DnsServer"]["Branding"];
            Assert.Equal("Acme Corp DNS", section["SiteTitle"].GetValue<string>());
            Assert.Equal(ValidPngLogoDataUri, section["LogoDataUri"].GetValue<string>());
        }
        finally
        {
            await client.SendAsync(
                PatchRequest(
                    "/api/branding-settings",
                    new { siteTitle = (string)null, logoDataUri = (string)null }
                )
            );
        }
    }

    [Fact]
    public async Task Patch_WithHostnameAndChaosToggle_WritesBothToTheAuthoritativeSection()
    {
        HttpClient client = await CreateAuthenticatedAdminClientAsync();

        try
        {
            HttpResponseMessage response = await client.SendAsync(
                PatchRequest(
                    "/api/branding-settings",
                    new { hostnameString = "acme-ns1", enableChaosVersionQueries = true }
                )
            );
            response.EnsureSuccessStatusCode();

            // This checks only the raw configuration file, never a live GET immediately after
            // this same PATCH -- NinlilDnsWebApplicationFactory's own isolated runtime settings
            // file is never separately registered as its own reload-on-change IConfiguration
            // source, a real, confirmed test-harness limitation TsigSettingsControllerTests'/
            // AppearanceSettingsControllerTests' own doc comments already document. Genuine
            // IOptionsMonitor hot-reload for AuthoritativeServerOptions is proved instead,
            // directly, by AuthoritativeQueryResolverChaosHotReloadTests (in
            // Ninlil.Dns.Server.Authoritative.Tests).
            JsonObject root = (JsonObject)
                JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
            JsonObject section = (JsonObject)root["DnsServer"]["Authoritative"];
            Assert.Equal("acme-ns1", section["HostnameString"].GetValue<string>());
            Assert.True(section["EnableChaosVersionQueries"].GetValue<bool>());
        }
        finally
        {
            await client.SendAsync(
                PatchRequest(
                    "/api/branding-settings",
                    new
                    {
                        hostnameString = Environment.MachineName,
                        enableChaosVersionQueries = false,
                    }
                )
            );
        }
    }

    [Fact]
    public async Task Patch_WithALogoThatIsNotWellFormed_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedAdminClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/branding-settings", new { logoDataUri = "not-a-data-uri" })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithALogoThatIsNotAnImage_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedAdminClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest(
                "/api/branding-settings",
                new { logoDataUri = "data:text/plain;base64,aGVsbG8=" }
            )
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithABlankHostname_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedAdminClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/branding-settings", new { hostnameString = "   " })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAnEmptyBody_ReturnsOkWithoutWritingAnything()
    {
        HttpClient client = await CreateAuthenticatedAdminClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/branding-settings", new { })
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Contains("nothing to change", result.Note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BrandingSettingsEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/branding-settings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetEffective_AnOrdinaryUserWithNoBrandingPermission_CanStillReadIt()
    {
        HttpClient adminClient = await CreateAuthenticatedAdminClientAsync();

        try
        {
            // This reads the current live value directly (never assumes a specific value written
            // by a PATCH earlier in this same test would already be observable here -- see
            // Patch_WithHostnameAndChaosToggle_WritesBothToTheAuthoritativeSection's own comment
            // on why a live GET can't reliably observe a same-process PATCH in this shared
            // harness), so this test's own real point -- that an ordinary user with no
            // branding:read permission is nonetheless served the exact same current value through
            // the self-service action -- holds regardless of what that value currently is.
            HttpResponseMessage adminGetResponse = await adminClient.GetAsync(
                "/api/branding-settings"
            );
            adminGetResponse.EnsureSuccessStatusCode();
            BrandingSettingsDetail adminDetail =
                await adminGetResponse.Content.ReadFromJsonAsync<BrandingSettingsDetail>();

            HttpClient userClient = await CreateOrdinaryUserClientAsync(
                Guid.NewGuid().ToString("N")
            );

            // Confirms this ordinary user genuinely lacks branding:read -- the admin-only action
            // is forbidden...
            HttpResponseMessage adminActionResponse = await userClient.GetAsync(
                "/api/branding-settings"
            );
            Assert.Equal(HttpStatusCode.Forbidden, adminActionResponse.StatusCode);

            // ...yet the self-service "effective" action, gated by [Authorize] alone, still
            // succeeds for that same permission-less caller and reports the exact same live
            // values the admin-only action just reported.
            HttpResponseMessage effectiveResponse = await userClient.GetAsync(
                "/api/branding-settings/effective"
            );
            effectiveResponse.EnsureSuccessStatusCode();
            BrandingEffectiveDetail effective =
                await effectiveResponse.Content.ReadFromJsonAsync<BrandingEffectiveDetail>();
            Assert.Equal(adminDetail.SiteTitle, effective.SiteTitle);
            Assert.Equal(adminDetail.LogoDataUri, effective.LogoDataUri);
        }
        finally
        {
            await adminClient.SendAsync(
                PatchRequest("/api/branding-settings", new { siteTitle = (string)null })
            );
        }
    }

    [Fact]
    public async Task GetEffective_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/branding-settings/effective");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
