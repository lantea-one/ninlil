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
///     This controller reads and writes <see cref="SecondaryZoneRefreshOptions" />: how often the
///     background loop checks whether a secondary zone is due for a refresh, and how long to wait
///     before retrying a zone that has no SOA yet or whose primaries were unreachable. Reading
///     needs <c>secondaryzonerefreshsettings:read</c> and writing needs
///     <c>secondaryzonerefreshsettings:write</c>.
///     <para>
///         <b>Every field takes effect without a restart.</b> The singleton
///         <c>SecondaryZoneRefreshService</c> reads <see cref="IOptionsMonitor{TOptions}" />'s
///         <c>CurrentValue</c> on every pass of its loop.
///     </para>
/// </summary>
/// <param name="secondaryZoneRefreshOptions">The current secondary-zone refresh settings.</param>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/secondary-zone-refresh-settings")]
public class SecondaryZoneRefreshSettingsController(
    IOptionsMonitor<SecondaryZoneRefreshOptions> secondaryZoneRefreshOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action returns the current secondary-zone refresh settings. Requires
    ///     <c>secondaryzonerefreshsettings:read</c>.
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
    ///     <c>DnsServer:Authoritative:SecondaryZoneRefresh</c> as an RFC 7396 merge patch, through
    ///     <see cref="PresetConfigurationFileApplier" />. Every field takes effect without a
    ///     restart. Returns 400 for a value that is not a positive TimeSpan. Requires
    ///     <c>secondaryzonerefreshsettings:write</c>.
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

        // Both intervals feed timers and delays, so they are capped at the timer limit.
        if (
            patch.TryGetMergePatchDuration(
                "pollInterval",
                out TimeSpan pollInterval,
                TimeSpan.FromMilliseconds(1),
                JsonElementMergePatchExtension.TimerMaximum
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    SecondaryZoneRefreshOptions.SectionName,
                    nameof(SecondaryZoneRefreshOptions.PollInterval),
                    pollInterval.ToString()
                )
            );

        if (
            patch.TryGetMergePatchDuration(
                "defaultRetryInterval",
                out TimeSpan defaultRetryInterval,
                TimeSpan.FromMilliseconds(1),
                JsonElementMergePatchExtension.TimerMaximum
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    SecondaryZoneRefreshOptions.SectionName,
                    nameof(SecondaryZoneRefreshOptions.DefaultRetryInterval),
                    defaultRetryInterval.ToString()
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
}
