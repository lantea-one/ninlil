using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Authoritative.Notify;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Examples.NotifySettings;
using Ninlil.Web.Examples.Settings;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller reads and writes this host's own <see cref="NotifyOptions" /> -- how many
///     times, how often, and to what port this server sends RFC 1996 NOTIFY messages to a zone's
///     configured secondaries -- `appsettings.json`-only until this pass. Every action requires
///     the <c>notifysettings:read</c> or <c>notifysettings:write</c> permission.
///     <para>
///         <b>Every field takes effect immediately, with no restart.</b> <c>UdpNotifySender</c>
///         used to read <see cref="NotifyOptions" /> via the snapshotted
///         <see cref="IOptions{TOptions}" />, cached for the life of the process the first time
///         it was read -- a real functional gap beyond REST/UI exposure, the same shape already
///         found and fixed elsewhere in this project for other options classes, converted here
///         the same way: <c>UdpNotifySender</c> now reads <see cref="IOptionsMonitor{TOptions}" />'s
///         own <c>CurrentValue</c> fresh on every single NOTIFY attempt, even though a fresh
///         instance of that (scoped) class is itself constructed per attempt -- the plain,
///         snapshotted <see cref="IOptions{TOptions}" /> it used before this fix would still have
///         handed every one of those fresh instances back the exact same process-wide-cached
///         value.
///     </para>
/// </summary>
/// <param name="notifyOptions">This host's own currently-effective NOTIFY settings, live.</param>
/// <param name="runtimeConfigurationOptions">The JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/notify-settings")]
public class NotifySettingsController(
    IOptionsMonitor<NotifyOptions> notifyOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action reads this host's own currently-effective (live, even without a restart)
    ///     NOTIFY settings.
    /// </summary>
    [HttpGet]
    [RequirePermission("notifysettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(NotifySettingsDetailExample))]
    public ActionResult<NotifySettingsDetail> Get() =>
        Ok(
            new NotifySettingsDetail(
                notifyOptions.CurrentValue.MaxAttempts,
                notifyOptions.CurrentValue.ResponseTimeout,
                notifyOptions.CurrentValue.RetryDelay,
                notifyOptions.CurrentValue.SecondaryPort
            )
        );

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:Authoritative:Notify</c> via
    ///     <see cref="PresetConfigurationFileApplier" />'s own generic settings-list overload, RFC
    ///     7396 merge-patch style. Every field this options class exposes hot-reloads immediately
    ///     -- see this class's own doc comment.
    /// </summary>
    [HttpPatch]
    [RequirePermission("notifysettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(NotifySettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];

        if (patch.TryGetMergePatchField("maxAttempts", out JsonElement maxAttemptsElement))
        {
            int maxAttempts = maxAttemptsElement.GetInt32();

            if (maxAttempts < 1)
                throw new ValidationException("'maxAttempts' must be at least 1.");

            settings.Add(
                new PresetConfigurationSetting(
                    NotifyOptions.SectionName,
                    nameof(NotifyOptions.MaxAttempts),
                    maxAttempts
                )
            );
        }

        if (patch.TryGetMergePatchField("responseTimeout", out JsonElement responseTimeoutElement))
            settings.Add(
                new PresetConfigurationSetting(
                    NotifyOptions.SectionName,
                    nameof(NotifyOptions.ResponseTimeout),
                    ParsePositiveTimeSpan(responseTimeoutElement, "responseTimeout").ToString()
                )
            );

        if (patch.TryGetMergePatchField("retryDelay", out JsonElement retryDelayElement))
            settings.Add(
                new PresetConfigurationSetting(
                    NotifyOptions.SectionName,
                    nameof(NotifyOptions.RetryDelay),
                    ParsePositiveTimeSpan(retryDelayElement, "retryDelay").ToString()
                )
            );

        if (patch.TryGetMergePatchField("secondaryPort", out JsonElement secondaryPortElement))
        {
            int secondaryPort = secondaryPortElement.GetInt32();

            if (secondaryPort is < 1 or > 65535)
                throw new ValidationException("'secondaryPort' must be between 1 and 65535.");

            settings.Add(
                new PresetConfigurationSetting(
                    NotifyOptions.SectionName,
                    nameof(NotifyOptions.SecondaryPort),
                    secondaryPort
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
    ///     This method parses a merge-patch field expected to be a positive <see cref="TimeSpan" />,
    ///     matching this project's own established convention of a small, controller-local helper
    ///     over a shared one (see <c>TryGetDuration</c>'s own several independent copies already
    ///     in this codebase).
    /// </summary>
    private static TimeSpan ParsePositiveTimeSpan(JsonElement element, string fieldName)
    {
        if (!TimeSpan.TryParse(element.GetString(), out TimeSpan parsed) || parsed <= TimeSpan.Zero)
            throw new ValidationException(
                $"'{fieldName}' must be a valid, positive TimeSpan (for example \"00:00:03\")."
            );

        return parsed;
    }
}
