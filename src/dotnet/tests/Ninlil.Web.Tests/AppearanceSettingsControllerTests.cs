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
///     This class covers <see cref="AppearanceSettingsController" /> over HTTP on the shared
///     in-process host: the two admin actions for the global default palette and mode (signed in
///     as the bootstrap administrator), and the two self-service actions for a user's own
///     override (signed in as a new user with no permissions, created per test). Admin patches are
///     checked by reading the host's temporary settings file
///     (<see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />) and the
///     returned note. <c>AppearanceOptions</c> is read through <c>IOptionsMonitor</c>, so a write
///     needs no restart; <see cref="AppearanceOptionsHotReloadTests" /> checks that reload.
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
    ///     This method creates a new user with no permissions and returns a client signed in as
    ///     it, so preference writes never touch the bootstrap administrator that other tests use.
    ///     It follows the setup in <c>TwoFactorRecoveryCodeTests.CreateTwoFactorEnrolledUserAsync</c>.
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
        // The host's settings file is not a configuration source, so this returns the
        // compiled-in defaults even after another test has patched the settings.
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

        // No restart is needed, because AppearanceOptions is read through IOptionsMonitor.
        // HotReloadedNote also mentions "restart" (to say none is needed), so the test compares
        // the constant rather than searching for a word.
        Assert.Equal(RuntimeSettingsWriteResult.HotReloadedNote, patchResult.Note);
        Assert.Contains("immediately", patchResult.Note, StringComparison.OrdinalIgnoreCase);

        // The write landed in the host's settings file. The reload itself is checked by
        // AppearanceOptionsHotReloadTests.
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

        // A new account has no override, so both fields are null and the effective values are
        // the current global default.
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

        // Under RFC 7396 merge-patch, a field present with a JSON null clears the override; an
        // absent field means no change.
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
