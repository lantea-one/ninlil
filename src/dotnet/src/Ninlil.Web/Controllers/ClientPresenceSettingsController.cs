using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.ClientIdentity.Presence;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Examples.ClientPresence;
using Ninlil.Web.Examples.Settings;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller exposes <see cref="ClientPresenceOptions" />: the online window and the
///     sampling and flush intervals behind passive client presence tracking, as a readable,
///     merge-patchable resource. Reading needs <c>clientpresencesettings:read</c> and writing
///     needs <c>clientpresencesettings:write</c>.
///     <para>
///         Every value is read through <see cref="IOptionsMonitor{TOptions}" /> each time it is
///         used, so a write takes effect without a restart: the online window on the next client
///         read, and the intervals at each background loop's next pass.
///     </para>
/// </summary>
/// <param name="presenceOptions">The current presence options.</param>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file a patch is merged into.</param>
[ApiController]
[Route("api/client-presence-settings")]
public class ClientPresenceSettingsController(
    IOptionsMonitor<ClientPresenceOptions> presenceOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action returns the current client presence settings. Requires
    ///     <c>clientpresencesettings:read</c>.
    /// </summary>
    [HttpGet]
    [RequirePermission("clientpresencesettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ClientPresenceSettingsDetailExample))]
    public ActionResult<ClientPresenceSettingsDetail> Get()
    {
        ClientPresenceOptions options = presenceOptions.CurrentValue;
        return Ok(
            new ClientPresenceSettingsDetail(
                options.OnlineWindow,
                options.FlushInterval,
                options.EnableNeighborTableSignal,
                options.NeighborTablePollInterval
            )
        );
    }

    /// <summary>
    ///     This action applies an RFC 7396 merge patch to the client presence settings. Returns 400
    ///     for an interval that is not a positive TimeSpan or a non-boolean
    ///     <c>enableNeighborTableSignal</c>. Requires <c>clientpresencesettings:write</c>.
    /// </summary>
    /// <param name="patch">The merge patch.</param>
    /// <returns>A note describing how the change takes effect.</returns>
    [HttpPatch]
    [RequirePermission("clientpresencesettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(ClientPresenceSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];

        // The online window is an age compared against timestamps; the other two intervals feed
        // timers, so they are capped at the timer limit.
        foreach (
            (string field, string property, TimeSpan maximum) in new[]
            {
                (
                    "onlineWindow",
                    nameof(ClientPresenceOptions.OnlineWindow),
                    TimeSpan.FromDays(36500)
                ),
                (
                    "flushInterval",
                    nameof(ClientPresenceOptions.FlushInterval),
                    JsonElementMergePatchExtension.TimerMaximum
                ),
                (
                    "neighborTablePollInterval",
                    nameof(ClientPresenceOptions.NeighborTablePollInterval),
                    JsonElementMergePatchExtension.TimerMaximum
                ),
            }
        )
        {
            if (
                !patch.TryGetMergePatchDuration(
                    field,
                    out TimeSpan interval,
                    TimeSpan.FromMilliseconds(1),
                    maximum
                )
            )
                continue;

            settings.Add(
                new PresetConfigurationSetting(
                    ClientPresenceOptions.SectionName,
                    property,
                    interval.ToString()
                )
            );
        }

        if (patch.TryGetMergePatchBoolean("enableNeighborTableSignal", out bool enable))
            settings.Add(
                new PresetConfigurationSetting(
                    ClientPresenceOptions.SectionName,
                    nameof(ClientPresenceOptions.EnableNeighborTableSignal),
                    enable
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
