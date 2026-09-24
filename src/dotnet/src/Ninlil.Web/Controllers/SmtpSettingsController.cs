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
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller reads and writes this host's own <see cref="EmailSenderOptions" /> -- the
///     SMTP server that already, genuinely delivers e-mail-based two-factor authentication codes
///     (via <c>SmtpEmailSender</c>) but, before this controller existed, had zero admin-facing
///     configuration surface: setting it up required hand-editing the configuration file's own
///     <c>Email</c> section and restarting. This is the prerequisite a future self-service Forgot
///     Password flow needs, since sending a real reset-token e-mail needs a real, admin-configured
///     SMTP server to send it through. Every action requires the <c>smtpsettings:read</c> or
///     <c>smtpsettings:write</c> permission.
///     <para>
///         <b>Every field takes effect immediately, with no restart.</b> <c>SmtpEmailSender</c>
///         used to read <see cref="EmailSenderOptions" /> via the snapshotted
///         <see cref="IOptions{TOptions}" />, cached for the life of the process the first time it
///         was read -- a real functional gap beyond REST/UI exposure, the same shape already
///         found and fixed elsewhere in this project for other options classes, converted here the
///         same way: <c>SmtpEmailSender</c> now reads <see cref="IOptionsMonitor{TOptions}" />'s
///         own <c>CurrentValue</c> fresh on every <c>SendAsync</c> call. Unlike a transport
///         listener's own bound socket, an SMTP send opens (and closes) a brand new connection
///         every single time, so there is no "read once at StartAsync" concern here at all -- a
///         write this controller's own <see cref="Patch" /> action merges into the configuration
///         file is honored by the very next e-mail sent.
///     </para>
/// </summary>
/// <param name="emailOptions">This host's own currently-effective SMTP settings, live.</param>
/// <param name="runtimeConfigurationOptions">The JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/smtp-settings")]
public class SmtpSettingsController(
    IOptionsMonitor<EmailSenderOptions> emailOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action reads this host's own currently-effective (live, even without a restart)
    ///     SMTP settings. <see cref="EmailSenderOptions.Password" /> is never returned -- see
    ///     <see cref="SmtpSettingsDetail.PasswordConfigured" />'s own doc comment.
    /// </summary>
    [HttpGet]
    [RequirePermission("smtpsettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(SmtpSettingsDetailExample))]
    public ActionResult<SmtpSettingsDetail> Get() => Ok(ToDetail(emailOptions.CurrentValue));

    /// <summary>
    ///     This action merges a partial update into <c>Email</c> via
    ///     <see cref="PresetConfigurationFileApplier" />'s own generic settings-list overload, RFC
    ///     7396 merge-patch style: a field absent from <paramref name="patch" /> is left exactly as
    ///     it was in the file -- including <c>password</c>, letting an operator change, say, only
    ///     <c>smtpHost</c> without ever having to resupply a credential this endpoint's own
    ///     <see cref="Get" /> action never reveals back to them. Every field hot-reloads
    ///     immediately -- see this class's own doc comment.
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

        // A blank string is accepted here (unlike 'smtpHost') -- it clears a previously-configured
        // password, moving the server back to anonymous-connection mode alongside a blank
        // 'username'.
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

        if (settings.Count == 0)
            return Ok(new RuntimeSettingsWriteResult("Nothing to change -- the patch was empty."));

        await PresetConfigurationFileApplier.ApplyAsync(
            settings,
            runtimeConfigurationOptions.ConfigurationFilePath
        );

        return Ok(new RuntimeSettingsWriteResult(RuntimeSettingsWriteResult.HotReloadedNote));
    }

    /// <summary>
    ///     This method applies the same lightweight e-mail-address sanity check
    ///     <see cref="Controllers.AcmeSettingsController" />'s own <c>contactEmails</c> validation
    ///     already establishes for this project -- real RFC 5322 validation happens only when the
    ///     address is actually used to send a message.
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
    ///     This method builds <see cref="SmtpSettingsDetail" /> from a live
    ///     <see cref="EmailSenderOptions" /> snapshot.
    /// </summary>
    private static SmtpSettingsDetail ToDetail(EmailSenderOptions options) =>
        new(
            options.SmtpHost,
            options.SmtpPort,
            options.UseTls,
            options.Username,
            !string.IsNullOrEmpty(options.Password),
            options.FromAddress,
            options.FromDisplayName
        );
}
