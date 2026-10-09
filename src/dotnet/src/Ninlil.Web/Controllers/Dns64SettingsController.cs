using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Core.Dns64;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Examples.Dns64;
using Ninlil.Web.Examples.Settings;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller reads and writes <see cref="Dns64Options" />: whether RFC 6147 DNS64
///     synthesis is on and which prefix it uses. Reading needs <c>dns64settings:read</c> and
///     writing needs <c>dns64settings:write</c>.
///     <para>
///         Every field takes effect at once, with no restart: <c>Dns64SynthesisService</c> reads
///         <see cref="Dns64Options" /> through <see cref="IOptionsMonitor{TOptions}" />.
///     </para>
/// </summary>
/// <param name="dns64Options">The current DNS64 settings.</param>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/dns64-settings")]
public class Dns64SettingsController(
    IOptionsMonitor<Dns64Options> dns64Options,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action returns the current DNS64 settings. Requires <c>dns64settings:read</c>.
    /// </summary>
    [HttpGet]
    [RequirePermission("dns64settings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(Dns64SettingsDetailExample))]
    public ActionResult<Dns64SettingsDetail> Get() =>
        Ok(
            new Dns64SettingsDetail(
                dns64Options.CurrentValue.Enable,
                dns64Options.CurrentValue.SynthesisPrefix
            )
        );

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:Dns64</c> as an RFC 7396 merge
    ///     patch, through <see cref="PresetConfigurationFileApplier" />. A field absent from
    ///     <paramref name="patch" /> keeps its value. <see cref="Dns64Options.SynthesisPrefix" />
    ///     is checked against the same RFC 6052 §2.2 rule
    ///     <c>Dns64SynthesisService.TryParsePrefix</c> applies (an IPv6 prefix of /32, /40, /48,
    ///     /56, /64 or /96), so a bad prefix returns 400 here instead of quietly disabling
    ///     synthesis later. Requires <c>dns64settings:write</c>.
    /// </summary>
    [HttpPatch]
    [RequirePermission("dns64settings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(Dns64SettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];

        if (patch.TryGetMergePatchBoolean("enable", out bool enable))
            settings.Add(
                new PresetConfigurationSetting(
                    Dns64Options.SectionName,
                    nameof(Dns64Options.Enable),
                    enable
                )
            );

        if (
            patch.TryGetMergePatchString(
                "synthesisPrefix",
                out string synthesisPrefix,
                allowNull: false
            )
        )
        {
            if (!Dns64SynthesisService.TryParsePrefix(synthesisPrefix, out _, out _))
                throw new ValidationException(
                    $"'{synthesisPrefix}' is not a legal RFC 6052 §2.2 DNS64 prefix -- it must be "
                        + "an IPv6 CIDR whose prefix length is one of /32, /40, /48, /56, /64, or /96."
                );

            settings.Add(
                new PresetConfigurationSetting(
                    Dns64Options.SectionName,
                    nameof(Dns64Options.SynthesisPrefix),
                    synthesisPrefix
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
