using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Presets;
using Ninlil.Dns.Server.Tsig.Options;
using Ninlil.Web.Examples.Settings;
using Ninlil.Web.Examples.TsigSettings;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller reads and writes this host's own <see cref="TsigServiceOptions" /> -- the
///     clock-skew tolerance (RFC 8945's own "fudge" field) written into every message this server
///     signs -- `appsettings.json`-only until this pass. Every action requires the
///     <c>tsigsettings:read</c> or <c>tsigsettings:write</c> permission. This is deliberately
///     separate from <c>TsigKeysController</c>, which manages the keys themselves; this controller
///     manages the one shared signing-behavior tuning knob every key uses alike.
///     <para>
///         <b>Its one field takes effect immediately, with no restart.</b> <c>TsigService</c> used
///         to read <see cref="TsigServiceOptions" /> via the snapshotted <see cref="IOptions{TOptions}" />,
///         cached for the life of the process the first time it was read -- a real functional gap
///         beyond REST/UI exposure, the same shape already found and fixed elsewhere in this
///         project for other options classes, converted here the same way: <c>TsigService</c> now reads
///         <see cref="IOptionsMonitor{TOptions}" />'s own <c>CurrentValue</c> fresh on every single
///         <c>SignAsync</c> call, even though a fresh instance of that (scoped) service is itself
///         constructed per call -- the plain, snapshotted <see cref="IOptions{TOptions}" /> it
///         used before this fix would still have handed every one of those fresh instances back
///         the exact same process-wide-cached value.
///     </para>
/// </summary>
/// <param name="tsigServiceOptions">This host's own currently-effective TSIG signing-behavior settings, live.</param>
/// <param name="runtimeConfigurationOptions">The JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/tsig-settings")]
public class TsigSettingsController(
    IOptionsMonitor<TsigServiceOptions> tsigServiceOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action reads this host's own currently-effective (live, even without a restart)
    ///     TSIG signing-behavior settings.
    /// </summary>
    [HttpGet]
    [RequirePermission("tsigsettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(TsigSettingsDetailExample))]
    public ActionResult<TsigSettingsDetail> Get() =>
        Ok(new TsigSettingsDetail(tsigServiceOptions.CurrentValue.DefaultFudgeSeconds));

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:Tsig</c> via
    ///     <see cref="PresetConfigurationFileApplier" />'s own generic settings-list overload, RFC
    ///     7396 merge-patch style. This options class's own single field hot-reloads immediately
    ///     -- see this class's own doc comment.
    /// </summary>
    [HttpPatch]
    [RequirePermission("tsigsettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(TsigSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];

        if (
            patch.TryGetMergePatchField(
                "defaultFudgeSeconds",
                out JsonElement defaultFudgeSecondsElement
            )
        )
        {
            int defaultFudgeSeconds = defaultFudgeSecondsElement.GetInt32();

            if (defaultFudgeSeconds is < 0 or > ushort.MaxValue)
                throw new ValidationException(
                    $"'defaultFudgeSeconds' must be between 0 and {ushort.MaxValue}."
                );

            settings.Add(
                new PresetConfigurationSetting(
                    TsigServiceOptions.SectionName,
                    nameof(TsigServiceOptions.DefaultFudgeSeconds),
                    (ushort)defaultFudgeSeconds
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
