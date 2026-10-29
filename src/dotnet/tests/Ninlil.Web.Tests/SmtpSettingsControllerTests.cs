using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Identity.Email;
using Ninlil.Dns.Identity.Options;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;
using Ninlil.Web.Options;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="SmtpSettingsController" /> (<c>EmailSenderOptions</c>, used for
///     e-mailed two-factor codes and password-reset links) over HTTP on the shared in-process
///     host, signed in as the bootstrap administrator: the compiled-in defaults, a patch checked
///     by reading the host's temporary settings file, the password's encryption there,
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

            // The raw body is read once: it must not echo the password in any field, and it
            // must still parse as the write result.
            string responseBody = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain("a-real-smtp-password", responseBody);
            RuntimeSettingsWriteResult result =
                JsonSerializer.Deserialize<RuntimeSettingsWriteResult>(
                    responseBody,
                    JsonSerializerOptions.Web
                );
            Assert.Equal(RuntimeSettingsWriteResult.HotReloadedNote, result.Note);

            // The file is checked for the stored values; a GET would still show the old values
            // (see NinlilDnsWebApplicationFactory.RuntimeConfigurationFilePath). The hot reload is
            // covered by SmtpEmailSenderHotReloadTests in Ninlil.Dns.Identity.Tests.
            JsonObject root = (JsonObject)
                JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
            JsonObject section = (JsonObject)root["Email"];
            Assert.Equal("smtp.example.invalid", section["SmtpHost"].GetValue<string>());
            Assert.Equal(2525, section["SmtpPort"].GetValue<int>());
            Assert.False(section["UseTls"].GetValue<bool>());
            Assert.Equal("notifications@example.invalid", section["Username"].GetValue<string>());

            // The password is encrypted at rest: the plaintext key is blank and the encrypted one
            // decrypts, with the host's key ring, to what was sent.
            Assert.Equal("", section["Password"].GetValue<string>());
            string passwordEncrypted = section["PasswordEncrypted"].GetValue<string>();
            Assert.NotEmpty(passwordEncrypted);
            Assert.NotEqual("a-real-smtp-password", passwordEncrypted);
            Assert.DoesNotContain(
                "a-real-smtp-password",
                await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath)
            );
            Assert.Equal(
                "a-real-smtp-password",
                new SmtpPasswordProtector(
                    factory.Services.GetRequiredService<IDataProtectionProvider>()
                ).Unprotect(passwordEncrypted)
            );
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
    public async Task Patch_WithABlankPassword_ClearsBothPasswordFields()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        try
        {
            (
                await client.SendAsync(
                    PatchRequest("/api/smtp-settings", new { password = "to-be-cleared" })
                )
            ).EnsureSuccessStatusCode();
            (
                await client.SendAsync(PatchRequest("/api/smtp-settings", new { password = "" }))
            ).EnsureSuccessStatusCode();

            JsonObject section = (JsonObject)
                JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath))[
                    "Email"
                ];
            Assert.Equal("", section["Password"].GetValue<string>());
            Assert.Equal("", section["PasswordEncrypted"].GetValue<string>());
        }
        finally
        {
            await client.SendAsync(PatchRequest("/api/smtp-settings", new { password = "" }));
        }
    }

    [Theory]
    [InlineData("plain", "", true)]
    [InlineData("", "ciphertext", true)]
    [InlineData("", "", false)]
    [InlineData(null, null, false)]
    public void Get_ReportsPasswordConfiguredWhenEitherPasswordFieldHasAValue(
        string password,
        string passwordEncrypted,
        bool expected
    )
    {
        // The test host does not reload its settings file, so the controller is built directly
        // over the options a reload would produce.
        SmtpSettingsController controller = new(
            new FixedOptionsMonitor(
                new EmailSenderOptions
                {
                    Password = password,
                    PasswordEncrypted = passwordEncrypted,
                }
            ),
            new RuntimeConfigurationOptions(),
            new EphemeralDataProtectionProvider()
        );

        SmtpSettingsDetail detail = (SmtpSettingsDetail)
            ((OkObjectResult)controller.Get().Result).Value;

        Assert.Equal(expected, detail.PasswordConfigured);
    }

    /// <summary>
    ///     This class is an <see cref="IOptionsMonitor{TOptions}" /> that always returns one
    ///     value.
    /// </summary>
    private sealed class FixedOptionsMonitor(EmailSenderOptions value)
        : IOptionsMonitor<EmailSenderOptions>
    {
        public EmailSenderOptions CurrentValue => value;

        public EmailSenderOptions Get(string name) => value;

        public IDisposable OnChange(Action<EmailSenderOptions, string> listener) => null;
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
    public async Task Patch_WithAPublicBaseUrlThatIsNotAnHttpUrl_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.SendAsync(
            PatchRequest("/api/smtp-settings", new { publicBaseUrl = "dns.example.com" })
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_WithAPublicBaseUrl_WritesItToTheConfigurationFile()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        try
        {
            HttpResponseMessage response = await client.SendAsync(
                PatchRequest(
                    "/api/smtp-settings",
                    new { publicBaseUrl = "https://dns.example.com" }
                )
            );
            response.EnsureSuccessStatusCode();

            JsonObject root = (JsonObject)
                JsonNode.Parse(await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
            Assert.Equal(
                "https://dns.example.com",
                root["Email"]["PublicBaseUrl"].GetValue<string>()
            );
        }
        finally
        {
            await client.SendAsync(PatchRequest("/api/smtp-settings", new { publicBaseUrl = "" }));
        }
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
