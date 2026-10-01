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
///     This controller reads and writes <see cref="TsigServiceOptions" />: the clock-skew
///     tolerance (the RFC 8945 "fudge" field) written into every message this server signs. The
///     keys themselves are managed by <c>TsigKeysController</c>. Reading needs
///     <c>tsigsettings:read</c> and writing needs <c>tsigsettings:write</c>.
///     <para>
///         <b>The field takes effect at once, with no restart.</b> <c>TsigService</c> reads
///         <see cref="IOptionsMonitor{TOptions}" />'s <c>CurrentValue</c> on every
///         <c>SignAsync</c> call.
///     </para>
/// </summary>
/// <param name="tsigServiceOptions">The current TSIG signing settings.</param>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/tsig-settings")]
public class TsigSettingsController(
    IOptionsMonitor<TsigServiceOptions> tsigServiceOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action returns the current TSIG signing settings. Requires
    ///     <c>tsigsettings:read</c>.
    /// </summary>
    [HttpGet]
    [RequirePermission("tsigsettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(TsigSettingsDetailExample))]
    public ActionResult<TsigSettingsDetail> Get() =>
        Ok(new TsigSettingsDetail(tsigServiceOptions.CurrentValue.DefaultFudgeSeconds));

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:Tsig</c> as an RFC 7396 merge
    ///     patch, through <see cref="PresetConfigurationFileApplier" />. The change takes effect at
    ///     once. Returns 400 when <c>defaultFudgeSeconds</c> is outside 0-65535. Requires
    ///     <c>tsigsettings:write</c>.
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
