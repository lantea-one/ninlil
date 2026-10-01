using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Presets;
using Ninlil.Dns.Server.SelfUpdate;
using Ninlil.Web.Examples.SelfUpdate;
using Ninlil.Web.Examples.Settings;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller reads and writes <see cref="SelfUpdateOptions" />: whether, how often and
///     against which feed the server checks for a newer release. Checking is on by default but
///     does nothing until <see cref="SelfUpdateOptions.ReleaseFeedUrl" /> is set (see
///     <see cref="SelfUpdateOptions" />). Reading needs <c>selfupdatesettings:read</c> and
///     writing needs <c>selfupdatesettings:write</c>.
///     <para>
///         Every field takes effect at the checker's next tick, with no restart:
///         <see cref="SelfUpdateCheckService" /> reads <see cref="SelfUpdateOptions" /> through
///         <see cref="IOptionsMonitor{TOptions}" />.
///     </para>
/// </summary>
/// <param name="selfUpdateOptions">The current self-update settings.</param>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/self-update-settings")]
public class SelfUpdateSettingsController(
    IOptionsMonitor<SelfUpdateOptions> selfUpdateOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action returns the current self-update settings. Requires
    ///     <c>selfupdatesettings:read</c>.
    /// </summary>
    [HttpGet]
    [RequirePermission("selfupdatesettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(SelfUpdateSettingsDetailExample))]
    public ActionResult<SelfUpdateSettingsDetail> Get() =>
        Ok(
            new SelfUpdateSettingsDetail(
                selfUpdateOptions.CurrentValue.EnableAutomaticCheck,
                selfUpdateOptions.CurrentValue.ReleaseFeedUrl,
                selfUpdateOptions.CurrentValue.CheckInterval
            )
        );

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:SelfUpdate</c> as an RFC 7396
    ///     merge patch, through <see cref="PresetConfigurationFileApplier" />. A field absent from
    ///     <paramref name="patch" /> keeps its value. <c>releaseFeedUrl</c> must be blank (no feed)
    ///     or an absolute http(s) URL; it is only checked for shape, not fetched, so a typo is
    ///     caught here rather than as a failing background check. Returns 400 for a bad URL or a
    ///     non-positive <c>checkInterval</c>. Requires <c>selfupdatesettings:write</c>.
    /// </summary>
    [HttpPatch]
    [RequirePermission("selfupdatesettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(SelfUpdateSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];

        if (
            patch.TryGetMergePatchField(
                "enableAutomaticCheck",
                out JsonElement enableAutomaticCheck
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    SelfUpdateOptions.SectionName,
                    nameof(SelfUpdateOptions.EnableAutomaticCheck),
                    enableAutomaticCheck.GetBoolean()
                )
            );

        if (patch.TryGetMergePatchField("releaseFeedUrl", out JsonElement releaseFeedUrlElement))
        {
            string releaseFeedUrl = releaseFeedUrlElement.GetString() ?? string.Empty;

            if (
                releaseFeedUrl.Length > 0
                && (
                    !Uri.TryCreate(releaseFeedUrl, UriKind.Absolute, out Uri parsed)
                    || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
                )
            )
                throw new ValidationException(
                    $"'{releaseFeedUrl}' is not a valid absolute HTTP or HTTPS URL."
                );

            settings.Add(
                new PresetConfigurationSetting(
                    SelfUpdateOptions.SectionName,
                    nameof(SelfUpdateOptions.ReleaseFeedUrl),
                    releaseFeedUrl
                )
            );
        }

        if (patch.TryGetMergePatchField("checkInterval", out JsonElement checkIntervalElement))
        {
            if (
                !TimeSpan.TryParse(checkIntervalElement.GetString(), out TimeSpan checkInterval)
                || checkInterval <= TimeSpan.Zero
            )
                throw new ValidationException(
                    "'checkInterval' must be a valid, positive TimeSpan (for example \"1.00:00:00\")."
                );

            settings.Add(
                new PresetConfigurationSetting(
                    SelfUpdateOptions.SectionName,
                    nameof(SelfUpdateOptions.CheckInterval),
                    checkInterval.ToString()
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
}
