using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Authoritative.Refresh;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Examples.SecondaryZoneRefreshSettings;
using Ninlil.Web.Examples.Settings;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller reads and writes this host's own <see cref="SecondaryZoneRefreshOptions" />
///     -- how often the background loop that checks whether a secondary zone is due for a refresh
///     wakes up -- `appsettings.json`-only until this pass. Every action requires the
///     <c>secondaryzonerefreshsettings:read</c> or <c>secondaryzonerefreshsettings:write</c>
///     permission.
///     <para>
///         <b>Every field takes effect immediately, with no restart.</b> <c>SecondaryZoneRefreshService</c>
///         is a singleton hosted service whose own loop body already re-read this options class's
///         properties on every iteration, but through the snapshotted <see cref="IOptions{TOptions}" />
///         -- computed once and cached for the life of the process -- so a configuration write
///         never actually reached a live loop, the same shape already found and fixed for
///         <c>DnssecResigningBackgroundService</c>. Converted here to
///         <see cref="IOptionsMonitor{TOptions}" />'s own <c>CurrentValue</c>, read fresh on every
///         loop iteration.
///     </para>
/// </summary>
/// <param name="secondaryZoneRefreshOptions">This host's own currently-effective secondary-zone-refresh-polling settings, live.</param>
/// <param name="runtimeConfigurationOptions">The JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/secondary-zone-refresh-settings")]
public class SecondaryZoneRefreshSettingsController(
    IOptionsMonitor<SecondaryZoneRefreshOptions> secondaryZoneRefreshOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action reads this host's own currently-effective (live, even without a restart)
    ///     secondary-zone-refresh-polling settings.
    /// </summary>
    [HttpGet]
    [RequirePermission("secondaryzonerefreshsettings:read")]
    [SwaggerResponseExample(
        StatusCodes.Status200OK,
        typeof(SecondaryZoneRefreshSettingsDetailExample)
    )]
    public ActionResult<SecondaryZoneRefreshSettingsDetail> Get() =>
        Ok(
            new SecondaryZoneRefreshSettingsDetail(
                secondaryZoneRefreshOptions.CurrentValue.PollInterval,
                secondaryZoneRefreshOptions.CurrentValue.DefaultRetryInterval
            )
        );

    /// <summary>
    ///     This action merges a partial update into
    ///     <c>DnsServer:Authoritative:SecondaryZoneRefresh</c> via
    ///     <see cref="PresetConfigurationFileApplier" />'s own generic settings-list overload, RFC
    ///     7396 merge-patch style. Every field this options class exposes hot-reloads immediately
    ///     -- see this class's own doc comment.
    /// </summary>
    [HttpPatch]
    [RequirePermission("secondaryzonerefreshsettings:write")]
    [SwaggerRequestExample(
        typeof(JsonElement),
        typeof(SecondaryZoneRefreshSettingsPatchRequestExample)
    )]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];

        if (patch.TryGetMergePatchField("pollInterval", out JsonElement pollIntervalElement))
            settings.Add(
                new PresetConfigurationSetting(
                    SecondaryZoneRefreshOptions.SectionName,
                    nameof(SecondaryZoneRefreshOptions.PollInterval),
                    ParsePositiveTimeSpan(pollIntervalElement, "pollInterval").ToString()
                )
            );

        if (
            patch.TryGetMergePatchField(
                "defaultRetryInterval",
                out JsonElement defaultRetryIntervalElement
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    SecondaryZoneRefreshOptions.SectionName,
                    nameof(SecondaryZoneRefreshOptions.DefaultRetryInterval),
                    ParsePositiveTimeSpan(defaultRetryIntervalElement, "defaultRetryInterval")
                        .ToString()
                )
            );

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
                $"'{fieldName}' must be a valid, positive TimeSpan (for example \"00:00:15\")."
            );

        return parsed;
    }
}
