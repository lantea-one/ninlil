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
///     This class covers <see cref="SmtpSettingsController" /> (<c>EmailSenderOptions</c>, used for
///     e-mailed two-factor codes and password-reset links) over HTTP on the shared in-process
///     host, signed in as the bootstrap administrator: the compiled-in defaults, a patch checked
///     by reading the host's temporary settings file
///     (<see cref="NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath" />) and the
///     returned note, and input validation.
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

        // The compiled-in defaults of EmailSenderOptions.
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

            // Only the file is checked; a GET would still show the old values (see
            // NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath). The hot reload is
            // covered by SmtpEmailSenderHotReloadTests in Ninlil.Dns.Identity.Tests. Despite the
            // test name, nothing checks that the password stays out of a response: the PATCH
            // response is only a note, and the GET never sees the stored password.
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
            // Write back the defaults the Get test checks. smtpHost, username and fromAddress
            // default to null and a blank value would be rejected, so they keep this test's
            // values.
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
