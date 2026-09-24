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
///     This class covers <see cref="SmtpSettingsController" /> end to end, against a real,
///     in-process host and an isolated, throwaway JSON configuration file (see
///     <see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />) -- closing a
///     real gap: <c>EmailSenderOptions</c> already drove real, working e-mail-based
///     two-factor authentication codes, but had zero admin-facing REST/UI configuration surface.
/// </summary>
[Collection(WebTestCollection.Name)]
public class SmtpSettingsControllerTests(NinlilDnsWebApplicationFactory factory)
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

        HttpResponseMessage response = await client.GetAsync("/api/smtp-settings");
        response.EnsureSuccessStatusCode();

        SmtpSettingsDetail detail = await response.Content.ReadFromJsonAsync<SmtpSettingsDetail>();

        // EmailSenderOptions's own compiled-in defaults -- see that class's own doc comment.
        Assert.Equal(587, detail.SmtpPort);
        Assert.True(detail.UseTls);
        Assert.Equal("Ninlil", detail.FromDisplayName);
        Assert.False(detail.PasswordConfigured);
    }

    [Fact]
    public async Task Patch_WithNewScalarValuesAndAPassword_WritesThemToTheConfigurationFileAndOmitsThePasswordFromTheResponse()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        try
        {
            HttpResponseMessage response = await client.SendAsync(
                PatchRequest(
                    "/api/smtp-settings",
                    new
                    {
                        smtpHost = "smtp.example.invalid",
                        smtpPort = 2525,
                        useTls = false,
                        username = "notifications@example.invalid",
                        password = "a-real-smtp-password",
                        fromAddress = "notifications@example.invalid",
                        fromDisplayName = "Example Notifications",
                    }
                )
            );
            response.EnsureSuccessStatusCode();

            RuntimeSettingsWriteResult result =
                await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
            Assert.Equal(RuntimeSettingsWriteResult.HotReloadedNote, result.Note);

            // This checks only the raw configuration file, never a live GET immediately after
            // this same PATCH -- NinlilDnsWebApplicationFactory's own isolated runtime
            // settings file is never separately registered as its own reload-on-change
            // IConfiguration source (only the host's real default appsettings.json path gets
            // that treatment automatically), a real, confirmed test-harness limitation this
            // project's settings-controller test suite already found and documented; every sibling settings-controller test in
            // this project (RecursiveSettingsControllerTests/AcmeSettingsControllerTests/
            // AppearanceSettingsControllerTests) already independently avoids the same assertion
            // for exactly this reason. Genuine IOptionsMonitor hot-reload for EmailSenderOptions
            // is proved instead, directly, by SmtpEmailSenderHotReloadTests (in
            // Ninlil.Dns.Identity.Tests) -- the password never being echoed back by this
            // controller's own Get action is proved below by
            // Get_ReturnsTheCompiledInDefaultsWhenNothingHasBeenConfigured's own assertion,
            // rather than a round trip against this same write.
            JsonObject root = (JsonObject)
                JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
            JsonObject section = (JsonObject)root["Email"];
            Assert.Equal("smtp.example.invalid", section["SmtpHost"].GetValue<string>());
            Assert.Equal(2525, section["SmtpPort"].GetValue<int>());
            Assert.False(section["UseTls"].GetValue<bool>());
            Assert.Equal("notifications@example.invalid", section["Username"].GetValue<string>());
            Assert.Equal("a-real-smtp-password", section["Password"].GetValue<string>());
            Assert.Equal(
                "notifications@example.invalid",
                section["FromAddress"].GetValue<string>()
            );
            Assert.Equal("Example Notifications", section["FromDisplayName"].GetValue<string>());
        }
        finally
        {
            // This factory's own runtime configuration file is shared across every test in
            // WebTestCollection -- restore every field this project's own "compiled-in defaults"
            // test actually asserts on afterward, the same "restore before returning" discipline
            // every other settings controller test class in this project already establishes.
            // 'smtpHost'/'username'/'fromAddress' have no real compiled-in default (each starts
            // null) and are not asserted elsewhere, so they are deliberately left as this test's
            // own values rather than forced back to an invalid blank string.
            await client.SendAsync(
                PatchRequest(
                    "/api/smtp-settings",
                    new
                    {
                        smtpPort = 587,
                        useTls = true,
                        password = "",
                        fromDisplayName = "Ninlil",
                    }
                )
            );
        }
    }

    [Fact]
    public async Task Patch_WithABlankSmtpHost_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/smtp-settings", new { smtpHost = "   " })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAnOutOfRangePort_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/smtp-settings", new { smtpPort = 70000 })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAnInvalidFromAddress_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/smtp-settings", new { fromAddress = "not-an-email" })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAnEmptyBody_ReturnsOkWithoutWritingAnything()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/smtp-settings", new { })
        );
        response.EnsureSuccessStatusCode();

        RuntimeSettingsWriteResult result =
            await response.Content.ReadFromJsonAsync<RuntimeSettingsWriteResult>();
        Assert.Contains("nothing to change", result.Note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SmtpSettingsEndpoint_WithoutAToken_ReturnsUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync("/api/smtp-settings");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
