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
///     This controller exposes <see cref="ClientPresenceOptions" /> -- the online window and the
///     sampling/flush cadence behind passive client presence tracking -- as a readable and
///     merge-patchable REST resource. Every action requires the
///     <c>clientpresencesettings:read</c> or <c>clientpresencesettings:write</c> permission.
///     <para>
///         Every value is consumed through <see cref="IOptionsMonitor{TOptions}" /> and re-read on
///         each use, so a write takes effect live, with no restart: the online window on the very
///         next client read, and the two intervals on each background loop's next iteration.
///     </para>
/// </summary>
/// <param name="presenceOptions">The live presence options.</param>
/// <param name="runtimeConfigurationOptions">Where the runtime configuration file a patch is written to lives.</param>
[ApiController]
[Route("api/client-presence-settings")]
public class ClientPresenceSettingsController(
    IOptionsMonitor<ClientPresenceOptions> presenceOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action returns the currently-effective client presence settings.
    /// </summary>
    /// <returns>The current settings.</returns>
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
    ///     This action applies a JSON merge patch to the client presence settings. Every interval
    ///     must be a positive <see cref="TimeSpan" />.
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

        // Validate every interval field up front, identically, before anything is written.
        foreach (
            (string field, string property) in new[]
            {
                ("onlineWindow", nameof(ClientPresenceOptions.OnlineWindow)),
                ("flushInterval", nameof(ClientPresenceOptions.FlushInterval)),
                (
                    "neighborTablePollInterval",
                    nameof(ClientPresenceOptions.NeighborTablePollInterval)
                ),
            }
        )
        {
            if (!patch.TryGetMergePatchField(field, out JsonElement element))
                continue;

            if (
                element.ValueKind != JsonValueKind.String
                || !TimeSpan.TryParse(element.GetString(), out TimeSpan interval)
                || interval <= TimeSpan.Zero
            )
                throw new ValidationException(
                    $"'{field}' must be a valid, positive TimeSpan (for example \"00:10:00\")."
                );

            settings.Add(
                new PresetConfigurationSetting(
                    ClientPresenceOptions.SectionName,
                    property,
                    interval.ToString()
                )
            );
        }

        if (patch.TryGetMergePatchField("enableNeighborTableSignal", out JsonElement enable))
        {
            if (enable.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new ValidationException("'enableNeighborTableSignal' must be a boolean.");

            settings.Add(
                new PresetConfigurationSetting(
                    ClientPresenceOptions.SectionName,
                    nameof(ClientPresenceOptions.EnableNeighborTableSignal),
                    enable.GetBoolean()
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
