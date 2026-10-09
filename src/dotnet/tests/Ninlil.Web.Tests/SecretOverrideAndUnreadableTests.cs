using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Email;
using Ninlil.Dns.Identity.Options;
using Ninlil.Dns.Identity.Sso;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Ninlil.Web.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers how the SSO and SMTP settings controllers report and handle secrets that
///     are overridden from outside the application or cannot be decrypted. Most tests build the
///     controller directly over hand-made options and a temporary settings file, because the
///     shared host's options cannot be changed per test (its settings file is not a configuration
///     source). Two tests use the shared host over HTTP for the 400 on a user name without a
///     password and assert that the settings file is unchanged afterwards.
/// </summary>
[Collection(WebTestCollection.Name)]
public sealed class SecretOverrideAndUnreadableTests(NinlilDnsWebApplicationFactory factory)
    : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-secret-overrides-{Guid.NewGuid():N}"
    );

    private readonly IDataProtectionProvider _hostKeyRing = new EphemeralDataProtectionProvider();

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private RuntimeConfigurationOptions NewRuntimeFile(string json = "{}")
    {
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory, "appsettings.json");
        File.WriteAllText(path, json);
        return new RuntimeConfigurationOptions { ConfigurationFilePath = path };
    }

    private string ForeignCiphertext(string value) =>
        new OidcClientSecretProtector(new EphemeralDataProtectionProvider()).Protect(value);

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement;

    private OidcSsoSettingsController SsoController(OidcSsoOptions options) =>
        new(
            new StaticOptionsMonitor<OidcSsoOptions>(options),
            new RuntimeConfigurationOptions(),
            _hostKeyRing
        );

    private SmtpSettingsController SmtpController(
        EmailSenderOptions options,
        RuntimeConfigurationOptions file = null
    ) =>
        new(
            new StaticOptionsMonitor<EmailSenderOptions>(options),
            file ?? new RuntimeConfigurationOptions(),
            _hostKeyRing
        );

    private static T Detail<T>(ActionResult<T> result) => (T)((OkObjectResult)result.Result).Value;

    [Fact]
    public void SsoGet_WithAPlaintextSecret_ReportsItAsFromTheEnvironmentAndConfigured()
    {
        OidcSsoSettingsDetail detail = Detail(
            SsoController(
                    new OidcSsoOptions
                    {
                        ClientSecret = "from-environment",
                        ClientSecretEncrypted = ForeignCiphertext("old"),
                    }
                )
                .Get()
        );

        Assert.True(detail.ClientSecretConfigured);
        Assert.True(detail.ClientSecretFromEnvironment);
        // The plaintext overrides the stored one, so the unreadable stored value does not matter.
        Assert.False(detail.ClientSecretUnreadable);
    }

    [Fact]
    public void SsoGet_WithAnUnreadableStoredSecret_ReportsItAsUnreadable()
    {
        OidcSsoSettingsDetail detail = Detail(
            SsoController(new OidcSsoOptions { ClientSecretEncrypted = ForeignCiphertext("old") })
                .Get()
        );

        Assert.True(detail.ClientSecretConfigured);
        Assert.False(detail.ClientSecretFromEnvironment);
        Assert.True(detail.ClientSecretUnreadable);
    }

    [Fact]
    public void SsoGet_WithAReadableStoredSecretOrNone_ReportsNeitherFlag()
    {
        OidcSsoSettingsDetail stored = Detail(
            SsoController(
                    new OidcSsoOptions
                    {
                        ClientSecretEncrypted = new OidcClientSecretProtector(_hostKeyRing).Protect(
                            "fine"
                        ),
                    }
                )
                .Get()
        );
        OidcSsoSettingsDetail none = Detail(SsoController(new OidcSsoOptions()).Get());

        Assert.True(stored.ClientSecretConfigured);
        Assert.False(stored.ClientSecretFromEnvironment);
        Assert.False(stored.ClientSecretUnreadable);
        Assert.False(none.ClientSecretConfigured);
        Assert.False(none.ClientSecretUnreadable);
    }

    [Fact]
    public async Task SsoPatch_WithAClientSecret_BlanksAPlaintextKeyLeftInTheFile()
    {
        RuntimeConfigurationOptions file = NewRuntimeFile(
            """{"Identity":{"Sso":{"ClientSecret":"typed-by-hand"}}}"""
        );
        OidcSsoSettingsController controller = new(
            new StaticOptionsMonitor<OidcSsoOptions>(new OidcSsoOptions()),
            file,
            _hostKeyRing
        );

        await controller.Patch(Json("""{"clientSecret":"entered-in-console"}"""));

        string text = await File.ReadAllTextAsync(file.ConfigurationFilePath);
        Assert.DoesNotContain("typed-by-hand", text);
        Assert.DoesNotContain("entered-in-console", text);
        JsonObject sso = (JsonObject)JsonNode.Parse(text)["Identity"]["Sso"];
        Assert.Equal("", sso["ClientSecret"].GetValue<string>());
        Assert.Equal(
            "entered-in-console",
            new OidcClientSecretProtector(_hostKeyRing).Unprotect(
                sso["ClientSecretEncrypted"].GetValue<string>()
            )
        );
    }

    [Fact]
    public void SmtpGet_WithAPlaintextPassword_ReportsItAsFromTheEnvironmentAndConfigured()
    {
        SmtpSettingsDetail detail = Detail(
            SmtpController(
                    new EmailSenderOptions
                    {
                        Password = "from-environment",
                        PasswordEncrypted = ForeignCiphertext("old"),
                    }
                )
                .Get()
        );

        Assert.True(detail.PasswordConfigured);
        Assert.True(detail.PasswordFromEnvironment);
        Assert.False(detail.PasswordUnreadable);
    }

    [Fact]
    public void SmtpGet_WithAnUnreadableStoredPassword_ReportsItAsUnreadable()
    {
        SmtpSettingsDetail detail = Detail(
            SmtpController(new EmailSenderOptions { PasswordEncrypted = ForeignCiphertext("old") })
                .Get()
        );

        Assert.True(detail.PasswordConfigured);
        Assert.False(detail.PasswordFromEnvironment);
        Assert.True(detail.PasswordUnreadable);
    }

    [Fact]
    public void SmtpGet_WithAReadableStoredPasswordOrNone_ReportsNeitherFlag()
    {
        SmtpSettingsDetail stored = Detail(
            SmtpController(
                    new EmailSenderOptions
                    {
                        PasswordEncrypted = new SmtpPasswordProtector(_hostKeyRing).Protect("fine"),
                    }
                )
                .Get()
        );
        SmtpSettingsDetail none = Detail(SmtpController(new EmailSenderOptions()).Get());

        Assert.True(stored.PasswordConfigured);
        Assert.False(stored.PasswordFromEnvironment);
        Assert.False(stored.PasswordUnreadable);
        Assert.False(none.PasswordConfigured);
        Assert.False(none.PasswordUnreadable);
    }

    [Fact]
    public async Task SmtpPatch_WithAUsernameAndNoPasswordFromAnySource_Throws400AndLeavesTheFile()
    {
        RuntimeConfigurationOptions file = NewRuntimeFile();
        SmtpSettingsController controller = SmtpController(new EmailSenderOptions(), file);

        ValidationException exception = await Assert.ThrowsAsync<ValidationException>(() =>
            controller.Patch(Json("""{"username":"mailer"}"""))
        );

        Assert.Contains("username", exception.Message);
        Assert.Equal("{}", await File.ReadAllTextAsync(file.ConfigurationFilePath));
    }

    [Fact]
    public async Task SmtpPatch_ClearingThePasswordWhileAUsernameIsSet_Throws400()
    {
        SmtpSettingsController controller = SmtpController(
            new EmailSenderOptions
            {
                Username = "mailer",
                PasswordEncrypted = new SmtpPasswordProtector(_hostKeyRing).Protect("stored"),
            },
            NewRuntimeFile()
        );

        await Assert.ThrowsAsync<ValidationException>(() =>
            controller.Patch(Json("""{"password":""}"""))
        );
    }

    [Fact]
    public async Task SmtpPatch_WithAUsernameAndAPasswordInTheSamePatch_Succeeds()
    {
        SmtpSettingsController controller = SmtpController(
            new EmailSenderOptions(),
            NewRuntimeFile()
        );

        ActionResult<RuntimeSettingsWriteResult> result = await controller.Patch(
            Json("""{"username":"mailer","password":"pw"}""")
        );

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Theory]
    [InlineData("stored")]
    [InlineData("plaintext")]
    public async Task SmtpPatch_ThatOmitsThePasswordWhileOneIsStored_Succeeds(string source)
    {
        EmailSenderOptions options = new() { Username = "mailer" };
        if (source == "stored")
            options.PasswordEncrypted = new SmtpPasswordProtector(_hostKeyRing).Protect("stored");
        else
            options.Password = "from-environment";
        RuntimeConfigurationOptions file = NewRuntimeFile();
        SmtpSettingsController controller = SmtpController(options, file);

        ActionResult<RuntimeSettingsWriteResult> result = await controller.Patch(
            Json("""{"smtpHost":"smtp.example.invalid"}""")
        );

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Contains(
            "smtp.example.invalid",
            await File.ReadAllTextAsync(file.ConfigurationFilePath)
        );
    }

    [Fact]
    public async Task SmtpPatch_ClearingTheUsernameWithoutAPassword_Succeeds()
    {
        SmtpSettingsController controller = SmtpController(
            new EmailSenderOptions { Username = "mailer" },
            NewRuntimeFile()
        );

        ActionResult<RuntimeSettingsWriteResult> result = await controller.Patch(
            Json("""{"username":null}""")
        );

        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task SmtpPatch_OverHttpWithAUsernameAndNoPassword_ReturnsBadRequestAndLeavesTheFile()
    {
        HttpClient client = factory.CreateClient();
        HttpResponseMessage login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(
                NinlilDnsWebApplicationFactory.AdminUserName,
                NinlilDnsWebApplicationFactory.AdminPassword
            )
        );
        login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            (await login.Content.ReadFromJsonAsync<LoginResponse>()).AccessToken
        );
        string before = await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath);

        using HttpRequestMessage request = new(HttpMethod.Patch, "/api/smtp-settings")
        {
            Content = new StringContent(
                """{"username":"mailer"}""",
                Encoding.UTF8,
                "application/json"
            ),
        };
        HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await File.ReadAllTextAsync(factory.RuntimeConfigurationFilePath));
    }
}
