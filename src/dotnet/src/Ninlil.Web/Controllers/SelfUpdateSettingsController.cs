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
///     This controller reads and writes this host's own <see cref="SelfUpdateOptions" /> -- the
///     REST management surface for whether, how often, and against what feed this server checks
///     for a newer release of itself. See <see cref="SelfUpdateOptions" />'s own doc comment for
///     why checking defaults on but has nothing to check against until an operator sets
///     <see cref="SelfUpdateOptions.ReleaseFeedUrl" />. No secret material is involved. Every
///     action requires the <c>selfupdatesettings:read</c> or <c>selfupdatesettings:write</c>
///     permission.
///     <para>
///         Every field takes effect on the very next check tick, with no restart:
///         <see cref="SelfUpdateOptions" /> is read via <see cref="IOptionsMonitor{TOptions}" />
///         by <see cref="SelfUpdateCheckService" />, the same live-reload shape
///         <see cref="Dns64SettingsController" /> already establishes for its own fields.
///     </para>
/// </summary>
/// <param name="selfUpdateOptions">This host's own currently-effective self-update settings, live.</param>
/// <param name="runtimeConfigurationOptions">The JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/self-update-settings")]
public class SelfUpdateSettingsController(
    IOptionsMonitor<SelfUpdateOptions> selfUpdateOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action reads this host's own currently-effective (live) self-update settings.
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
    ///     This action merges a partial update into <c>DnsServer:SelfUpdate</c> via
    ///     <see cref="PresetConfigurationFileApplier" />'s own generic settings-list overload, RFC
    ///     7396 merge-patch style: a field absent from <paramref name="patch" /> is left exactly as
    ///     it was in the file. <c>releaseFeedUrl</c> is validated as either blank (clearing it back
    ///     to "nothing to check against") or a well-formed absolute HTTP/HTTPS URL -- never
    ///     followed or fetched here, only shape-checked, so a typo is caught immediately rather
    ///     than surfacing only as a repeatedly-failing background check.
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
