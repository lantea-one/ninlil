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
///     This controller reads and writes this host's own <see cref="Dns64Options" /> -- the REST
///     management surface for this server's own RFC 6147 DNS64 synthesis behavior. No secret
///     material is involved. Every action requires the <c>dns64settings:read</c> or
///     <c>dns64settings:write</c> permission.
///     <para>
///         Every field takes effect immediately, with no restart:
///         <see cref="Dns64Options" /> is read via <see cref="IOptionsMonitor{TOptions}" /> by
///         <c>Dns64SynthesisService</c>, the same live-reload shape
///         <see cref="ClientSubnetSettingsController" /> already establishes for its own fields.
///     </para>
/// </summary>
/// <param name="dns64Options">This host's own currently-effective DNS64 settings, live.</param>
/// <param name="runtimeConfigurationOptions">The JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/dns64-settings")]
public class Dns64SettingsController(
    IOptionsMonitor<Dns64Options> dns64Options,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action reads this host's own currently-effective (live) DNS64 settings.
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
    ///     This action merges a partial update into <c>DnsServer:Dns64</c> via
    ///     <see cref="PresetConfigurationFileApplier" />'s own generic settings-list overload, RFC
    ///     7396 merge-patch style: a field absent from <paramref name="patch" /> is left exactly as
    ///     it was in the file. <see cref="Dns64Options.SynthesisPrefix" /> is validated here, at
    ///     write time, against the same RFC 6052 §2.2 rule
    ///     <c>Dns64SynthesisService.TryParsePrefix</c> enforces at synthesis time, so a
    ///     misconfigured prefix is rejected immediately rather than silently disabling synthesis
    ///     later.
    /// </summary>
    [HttpPatch]
    [RequirePermission("dns64settings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(Dns64SettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];

        if (patch.TryGetMergePatchField("enable", out JsonElement enable))
            settings.Add(
                new PresetConfigurationSetting(
                    Dns64Options.SectionName,
                    nameof(Dns64Options.Enable),
                    enable.GetBoolean()
                )
            );

        if (patch.TryGetMergePatchField("synthesisPrefix", out JsonElement synthesisPrefixElement))
        {
            string synthesisPrefix = synthesisPrefixElement.GetString();

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
