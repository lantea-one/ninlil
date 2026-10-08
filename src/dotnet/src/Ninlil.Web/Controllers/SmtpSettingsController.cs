using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Identity.Options;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Examples.Settings;
using Ninlil.Web.Examples.SmtpSettings;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Ninlil.Web.Services;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller reads and writes <see cref="EmailSenderOptions" />, the SMTP server that
///     <c>SmtpEmailSender</c> uses for e-mailed two-factor codes and password-reset links
///     (<c>AuthController.ForgotPassword</c> and the <c>EmailLink</c> mode of
///     <c>UsersController.ResetPassword</c>). Reading needs <c>smtpsettings:read</c> and writing
///     needs <c>smtpsettings:write</c>.
///     <para>
///         <b>Every field takes effect at once, with no restart.</b> <c>SmtpEmailSender</c> reads
///         <see cref="IOptionsMonitor{TOptions}" />'s <c>CurrentValue</c> on every send and opens a
///         new connection each time, so the next e-mail uses the new settings.
///     </para>
/// </summary>
/// <param name="emailOptions">The current SMTP settings.</param>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/smtp-settings")]
public class SmtpSettingsController(
    IOptionsMonitor<EmailSenderOptions> emailOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action returns the current SMTP settings. The password is never returned, only
    ///     whether one is set (see <see cref="SmtpSettingsDetail.PasswordConfigured" />). Requires
    ///     <c>smtpsettings:read</c>.
    /// </summary>
    [HttpGet]
    [RequirePermission("smtpsettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(SmtpSettingsDetailExample))]
    public ActionResult<SmtpSettingsDetail> Get() => Ok(ToDetail(emailOptions.CurrentValue));

    /// <summary>
    ///     This action merges a partial update into the <c>Email</c> section as an RFC 7396 merge
    ///     patch, through <see cref="PresetConfigurationFileApplier" />. A field absent from
    ///     <paramref name="patch" /> keeps its value, including <c>password</c>, so an operator can
    ///     change only <c>smtpHost</c> without resupplying a password <see cref="Get" /> never
    ///     shows. The password is stored in plain text in the configuration file. Every field takes
    ///     effect at once. Returns 400 for a blank host or display name, a port outside 1-65535, a
    ///     malformed from-address or a <c>publicBaseUrl</c> that is not an absolute http or https
    ///     URL. Requires <c>smtpsettings:write</c>.
    /// </summary>
    [HttpPatch]
    [RequirePermission("smtpsettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(SmtpSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];

        if (patch.TryGetMergePatchField("smtpHost", out JsonElement smtpHostElement))
        {
            string smtpHost = smtpHostElement.GetString();

            if (string.IsNullOrWhiteSpace(smtpHost))
                throw new ValidationException("'smtpHost' must not be blank.");

            settings.Add(
                new PresetConfigurationSetting(
                    EmailSenderOptions.SectionName,
                    nameof(EmailSenderOptions.SmtpHost),
                    smtpHost
                )
            );
        }

        if (patch.TryGetMergePatchField("smtpPort", out JsonElement smtpPortElement))
        {
            int smtpPort = smtpPortElement.GetInt32();

            if (smtpPort is < 1 or > 65535)
                throw new ValidationException("'smtpPort' must be between 1 and 65535.");

            settings.Add(
                new PresetConfigurationSetting(
                    EmailSenderOptions.SectionName,
                    nameof(EmailSenderOptions.SmtpPort),
                    smtpPort
                )
            );
        }

        if (patch.TryGetMergePatchField("useTls", out JsonElement useTlsElement))
            settings.Add(
                new PresetConfigurationSetting(
                    EmailSenderOptions.SectionName,
                    nameof(EmailSenderOptions.UseTls),
                    useTlsElement.GetBoolean()
                )
            );

        if (patch.TryGetMergePatchField("username", out JsonElement usernameElement))
            settings.Add(
                new PresetConfigurationSetting(
                    EmailSenderOptions.SectionName,
                    nameof(EmailSenderOptions.Username),
                    usernameElement.GetString()
                )
            );

        // A blank string is accepted here (unlike 'smtpHost'): it clears the password, which with a
        // blank 'username' means connecting without authentication.
        if (patch.TryGetMergePatchField("password", out JsonElement passwordElement))
            settings.Add(
                new PresetConfigurationSetting(
                    EmailSenderOptions.SectionName,
                    nameof(EmailSenderOptions.Password),
                    passwordElement.GetString()
                )
            );

        if (patch.TryGetMergePatchField("fromAddress", out JsonElement fromAddressElement))
        {
            string fromAddress = fromAddressElement.GetString();
            ValidateEmailAddress(fromAddress, "fromAddress");

            settings.Add(
                new PresetConfigurationSetting(
                    EmailSenderOptions.SectionName,
                    nameof(EmailSenderOptions.FromAddress),
                    fromAddress
                )
            );
        }

        if (patch.TryGetMergePatchField("fromDisplayName", out JsonElement fromDisplayNameElement))
        {
            string fromDisplayName = fromDisplayNameElement.GetString();

            if (string.IsNullOrWhiteSpace(fromDisplayName))
                throw new ValidationException("'fromDisplayName' must not be blank.");

            settings.Add(
                new PresetConfigurationSetting(
                    EmailSenderOptions.SectionName,
                    nameof(EmailSenderOptions.FromDisplayName),
                    fromDisplayName
                )
            );
        }

        // A blank value is accepted and clears the setting, which stops reset links being sent.
        if (patch.TryGetMergePatchField("publicBaseUrl", out JsonElement publicBaseUrlElement))
        {
            string publicBaseUrl = publicBaseUrlElement.GetString()?.Trim() ?? "";

            if (
                publicBaseUrl.Length > 0
                && !PasswordResetLinkSender.TryParsePublicBaseUrl(publicBaseUrl, out _)
            )
                throw new ValidationException(
                    $"'{publicBaseUrl}' is not a valid 'publicBaseUrl'. It must be an absolute "
                        + "http or https URL with no query string, such as "
                        + "'https://dns.example.com'."
                );

            settings.Add(
                new PresetConfigurationSetting(
                    EmailSenderOptions.SectionName,
                    nameof(EmailSenderOptions.PublicBaseUrl),
                    publicBaseUrl
                )
            );
        }

        if (settings.Count == 0)
            return Ok(new RuntimeSettingsWriteResult("Nothing to change -- the patch was empty."));

        await PresetConfigurationFileApplier.ApplyAsync(
            settings,
            runtimeConfigurationOptions.ConfigurationFilePath
        );

        return Ok(new RuntimeSettingsWriteResult(RuntimeSettingsWriteResult.HotReloadedNote));
    }

    /// <summary>
    ///     This method applies the same light e-mail-address check as the <c>contactEmails</c>
    ///     validation in <see cref="Controllers.AcmeSettingsController" />. Full RFC 5322
    ///     validation happens only when the address is used to send a message.
    /// </summary>
    private static void ValidateEmailAddress(string email, string fieldName)
    {
        if (
            string.IsNullOrWhiteSpace(email)
            || !email.Contains('@')
            || email.StartsWith('@')
            || email.EndsWith('@')
        )
            throw new ValidationException(
                $"'{email}' is not a valid e-mail address for '{fieldName}'."
            );
    }

    /// <summary>
    ///     This method builds <see cref="SmtpSettingsDetail" /> from the current
    ///     <see cref="EmailSenderOptions" />.
    /// </summary>
    private static SmtpSettingsDetail ToDetail(EmailSenderOptions options) =>
        new(
            options.SmtpHost,
            options.SmtpPort,
            options.UseTls,
            options.Username,
            !string.IsNullOrEmpty(options.Password),
            options.FromAddress,
            options.FromDisplayName,
            options.PublicBaseUrl
        );
}
