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
///     This class covers <see cref="BrandingSettingsController" /> over HTTP on the shared
///     in-process host, signed in as the bootstrap administrator: one <c>PATCH</c> that writes both
///     <c>BrandingOptions</c> (site title and logo) and the CHAOS-query fields of
///     <c>AuthoritativeServerOptions</c>, checked by reading the host's temporary settings file
///     (<see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />), plus input
///     validation and the self-service <c>effective</c> action, which a user with no permissions
///     may read. The hot reload is covered by <see cref="BrandingOptionsHotReloadTests" /> and,
///     for the CHAOS fields, <c>AuthoritativeQueryResolverChaosHotReloadTests</c> in
///     <c>Ninlil.Dns.Server.Authoritative.Tests</c>.
/// </summary>
[Collection(WebTestCollection.Name)]
public class BrandingSettingsControllerTests(NinlilDnsWebApplicationFactory factory)
{
    /// <summary>
    ///     This constant contains a valid data URI for a 1x1 transparent PNG, so the logo passes
    ///     <see cref="Ninlil.Common.Serializer.Model.DataUriModel" /> parsing for the right reason.
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
    ///     This method creates a new user with no permissions and returns a client signed in as
    ///     it, as <see cref="AppearanceSettingsControllerTests.CreateOrdinaryUserClientAsync" />
    ///     does. It shows that <see cref="BrandingSettingsController.GetEffective" /> needs no
    ///     permission, unlike <see cref="BrandingSettingsController.Get" />.
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

            // Only the file is checked; a GET would still show the old values (see
            // NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath).
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
            // Read the current value instead of assuming one, since a GET does not see PATCH
            // writes in this host. The point is that a user without branding:read is served the
            // same values through the self-service action, whatever they are.
            HttpResponseMessage adminGetResponse = await adminClient.GetAsync(
                "/api/branding-settings"
            );
            adminGetResponse.EnsureSuccessStatusCode();
            BrandingSettingsDetail adminDetail =
                await adminGetResponse.Content.ReadFromJsonAsync<BrandingSettingsDetail>();

            HttpClient userClient = await CreateOrdinaryUserClientAsync(
                Guid.NewGuid().ToString("N")
            );

            // The user lacks branding:read, so the admin action is forbidden...
            HttpResponseMessage adminActionResponse = await userClient.GetAsync(
                "/api/branding-settings"
            );
            Assert.Equal(HttpStatusCode.Forbidden, adminActionResponse.StatusCode);

            // ...but the "effective" action, which needs only [Authorize], succeeds and reports
            // the same values.
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
