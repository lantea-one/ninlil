using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="AppearanceSettingsController" /> end to end, against a real,
///     in-process host and an isolated, throwaway JSON configuration file (see
///     <see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />) -- the two
///     admin actions (global default palette/mode, closely mirroring
///     <see cref="RateLimitingSettingsControllerTests" />) and the two self-service actions (a
///     signed-in caller's own preference override, mirroring the dedicated-user pattern
///     <see cref="TwoFactorRecoveryCodeTests" /> already establishes). One property is unique to
///     this controller among every other runtime settings controller in this project: its own
///     write is visible on the very next read with no restart, since <c>AppearanceOptions</c> is
///     this codebase's first options class bound via <c>IOptionsMonitor</c> rather than the usual
///     <c>IOptions</c> -- this class only confirms the write itself and this controller's own
///     accurate no-restart-needed message; <see cref="AppearanceOptionsHotReloadTests" /> is the
///     dedicated test that actually proves the live reload happens, end to end, against the real
///     .NET configuration/options binding pipeline (see that class's own doc comment for why it
///     is not exercised through this shared HTTP fixture instead).
/// </summary>
[Collection(WebTestCollection.Name)]
public class AppearanceSettingsControllerTests(NinlilDnsWebApplicationFactory factory)
{
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
    ///     This method creates a fresh, dedicated, ordinary (non-administrative) user account and
    ///     returns a client already authenticated as it -- never the shared bootstrap
    ///     administrator, so this test's own preference writes can never affect any other test in
    ///     this shared fixture. Mirrors <c>TwoFactorRecoveryCodeTests.CreateTwoFactorEnrolledUserAsync</c>'s
    ///     own setup shape.
    /// </summary>
    private async Task<HttpClient> CreateOrdinaryUserClientAsync(string userNameSuffix)
    {
        HttpClient adminClient = await CreateAuthenticatedAdminClientAsync();

        string userName = $"appearance-test-user-{userNameSuffix}";
        const string password = "Appearance-Test-Password-1234!";

        HttpResponseMessage createResponse = await adminClient.PostAsJsonAsync(
            "/api/users",
            new CreateUserRequest(
                userName,
                password,
                $"{userName}@localhost.local",
                "Appearance Test User"
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
        // A dedicated, isolated runtime configuration file backs this factory (see its own doc
        // comment), so this is the very first read against it -- genuinely the compiled-in
        // AppearanceOptions defaults, not whatever an earlier test in this shared fixture wrote.
        HttpClient client = await CreateAuthenticatedAdminClientAsync();

        HttpResponseMessage response = await client.GetAsync("/api/appearance-settings");
        response.EnsureSuccessStatusCode();

        AppearanceSettingsDetail detail =
            await response.Content.ReadFromJsonAsync<AppearanceSettingsDetail>();

        Assert.Equal(UiThemePaletteEnum.Teal, detail.Palette);
        Assert.Equal(UiThemeModeEnum.System, detail.Mode);
    }

    [Fact]
    public async Task Patch_WithNewValues_WritesThemToTheConfigurationFileAndReportsNoRestartNeeded()
    {
        HttpClient client = await CreateAuthenticatedAdminClientAsync();

        HttpResponseMessage patchResponse = await client.SendAsync(
            PatchRequest("/api/appearance-settings", new { palette = "Amber", mode = "Dark" })
        );
        patchResponse.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult patchResult =
            await patchResponse.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();

        // The one behavior that sets this controller apart from every other runtime settings
        // controller in this project: no "restart required" caveat, because AppearanceOptions is
        // read back via IOptionsMonitor. HotReloadedNote's own text still mentions "restart" (to
        // say none is required), so this compares against the exact, distinct constant rather
        // than a loose substring check against RestartRequiredNote's own wording.
        Assert.Equal(RuntimeSettingsWriteResult.HotReloadedNote, patchResult.Note);
        Assert.Contains("immediately", patchResult.Note, StringComparison.OrdinalIgnoreCase);

        // The write actually landed in the configuration file this factory points every runtime
        // settings controller at. This factory's own isolated runtime settings file is
        // deliberately never wired back in as a live configuration source for this same
        // in-process host (see NinlilDnsWebApplicationFactory's own doc comment on why), so
        // the genuine end-to-end proof that a write here needs no restart lives in
        // AppearanceOptionsHotReloadTests instead, which reconstructs the real-deployment shape
        // directly (one file, both written to and bound live via IOptionsMonitor).
        JsonObject root = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
        JsonObject section = (JsonObject)root["DnsServer"]["Appearance"];
        Assert.Equal("Amber", section["Palette"].GetValue<string>());
        Assert.Equal("Dark", section["Mode"].GetValue<string>());
    }

    [Fact]
    public async Task Patch_WithAnInvalidPalette_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedAdminClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/appearance-settings", new { palette = "Chartreuse" })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAnInvalidMode_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedAdminClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/appearance-settings", new { mode = "Sepia" })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAnEmptyBody_ReturnsOkWithoutWritingAnything()
    {
        HttpClient client = await CreateAuthenticatedAdminClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/appearance-settings", new { })
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Contains("nothing to change", result.Note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AppearanceSettingsEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/appearance-settings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SelfService_GetMine_InheritsTheCurrentGlobalDefaultForABrandNewUser()
    {
        HttpClient adminClient = await CreateAuthenticatedAdminClientAsync();
        HttpResponseMessage globalResponse = await adminClient.GetAsync("/api/appearance-settings");
        globalResponse.EnsureSuccessStatusCode();
        AppearanceSettingsDetail globalDefault =
            await globalResponse.Content.ReadFromJsonAsync<AppearanceSettingsDetail>();

        HttpClient userClient = await CreateOrdinaryUserClientAsync(Guid.NewGuid().ToString("N"));

        HttpResponseMessage mineResponse = await userClient.GetAsync("/api/appearance-settings/me");
        mineResponse.EnsureSuccessStatusCode();
        UserAppearancePreferenceDetail mine =
            await mineResponse.Content.ReadFromJsonAsync<UserAppearancePreferenceDetail>();

        // A brand-new account has never set an override of its own -- both fields are null, and
        // the effective value inherited is exactly whatever this host's own current global
        // default is right now.
        Assert.Null(mine.Palette);
        Assert.Null(mine.Mode);
        Assert.Equal(globalDefault.Palette, mine.EffectivePalette);
        Assert.Equal(globalDefault.Mode, mine.EffectiveMode);
    }

    [Fact]
    public async Task SelfService_PatchMine_SetsAnOverride_AndGetMineReflectsIt()
    {
        HttpClient userClient = await CreateOrdinaryUserClientAsync(Guid.NewGuid().ToString("N"));

        HttpResponseMessage patchResponse = await userClient.SendAsync(
            PatchRequest("/api/appearance-settings/me", new { palette = "Rose", mode = "Dark" })
        );
        patchResponse.EnsureSuccessStatusCode();
        UserAppearancePreferenceDetail patched =
            await patchResponse.Content.ReadFromJsonAsync<UserAppearancePreferenceDetail>();

        Assert.Equal(UiThemePaletteEnum.Rose, patched.Palette);
        Assert.Equal(UiThemeModeEnum.Dark, patched.Mode);
        Assert.Equal(UiThemePaletteEnum.Rose, patched.EffectivePalette);
        Assert.Equal(UiThemeModeEnum.Dark, patched.EffectiveMode);

        HttpResponseMessage mineResponse = await userClient.GetAsync("/api/appearance-settings/me");
        mineResponse.EnsureSuccessStatusCode();
        UserAppearancePreferenceDetail mine =
            await mineResponse.Content.ReadFromJsonAsync<UserAppearancePreferenceDetail>();

        Assert.Equal(UiThemePaletteEnum.Rose, mine.Palette);
        Assert.Equal(UiThemeModeEnum.Dark, mine.Mode);
    }

    [Fact]
    public async Task SelfService_PatchMine_WithAnExplicitNull_ClearsTheOverrideBackToTheGlobalDefault()
    {
        HttpClient adminClient = await CreateAuthenticatedAdminClientAsync();
        HttpResponseMessage globalResponse = await adminClient.GetAsync("/api/appearance-settings");
        globalResponse.EnsureSuccessStatusCode();
        AppearanceSettingsDetail globalDefault =
            await globalResponse.Content.ReadFromJsonAsync<AppearanceSettingsDetail>();

        HttpClient userClient = await CreateOrdinaryUserClientAsync(Guid.NewGuid().ToString("N"));

        HttpResponseMessage setResponse = await userClient.SendAsync(
            PatchRequest("/api/appearance-settings/me", new { palette = "Emerald" })
        );
        setResponse.EnsureSuccessStatusCode();
        UserAppearancePreferenceDetail afterSet =
            await setResponse.Content.ReadFromJsonAsync<UserAppearancePreferenceDetail>();
        Assert.Equal(UiThemePaletteEnum.Emerald, afterSet.Palette);

        // A JSON `null` value for a present field, per this project's own RFC 7396 merge-patch
        // convention, clears the field's own override entirely -- not "no change" (an absent
        // field) and not "set it to some literal null palette" (which does not exist).
        HttpResponseMessage clearResponse = await userClient.SendAsync(
            new HttpRequestMessage(HttpMethod.Patch, "/api/appearance-settings/me")
            {
                Content = new StringContent(
                    """{"palette":null}""",
                    Encoding.UTF8,
                    "application/json"
                ),
            }
        );
        clearResponse.EnsureSuccessStatusCode();
        UserAppearancePreferenceDetail afterClear =
            await clearResponse.Content.ReadFromJsonAsync<UserAppearancePreferenceDetail>();

        Assert.Null(afterClear.Palette);
        Assert.Equal(globalDefault.Palette, afterClear.EffectivePalette);
    }

    [Fact]
    public async Task SelfService_PatchMine_WithAnInvalidMode_ReturnsBadRequest()
    {
        HttpClient userClient = await CreateOrdinaryUserClientAsync(Guid.NewGuid().ToString("N"));

        HttpResponseMessage response = await userClient.SendAsync(
            PatchRequest("/api/appearance-settings/me", new { mode = "Sepia" })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SelfServiceAppearanceEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/appearance-settings/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
