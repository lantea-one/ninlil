using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Core.Transport;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Examples.OutboundQuery;
using Ninlil.Web.Examples.Settings;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller reads and writes this host's own <see cref="OutboundQueryOptions" /> --
///     the REST management surface for this server's own outbound UDP source-port
///     randomization/pooling. No secret material is involved. Every action requires the
///     <c>outboundquerysettings:read</c> or <c>outboundquerysettings:write</c> permission.
///     <para>
///         Every field takes effect immediately, with no restart:
///         <see cref="OutboundQueryOptions" /> is read via <see cref="IOptionsMonitor{TOptions}" />
///         by <c>OutboundUdpSocketPool</c>, the same live-reload shape
///         <see cref="RecursiveSettingsController" /> already establishes for most of its own
///         fields.
///     </para>
/// </summary>
/// <param name="outboundQueryOptions">This host's own currently-effective outbound-query settings, live.</param>
/// <param name="runtimeConfigurationOptions">The JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/outbound-query-settings")]
public class OutboundQuerySettingsController(
    IOptionsMonitor<OutboundQueryOptions> outboundQueryOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action reads this host's own currently-effective (live) outbound-query settings.
    /// </summary>
    [HttpGet]
    [RequirePermission("outboundquerysettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(OutboundQuerySettingsDetailExample))]
    public ActionResult<OutboundQuerySettingsDetail> Get() =>
        Ok(
            new OutboundQuerySettingsDetail(
                outboundQueryOptions.CurrentValue.EnableSourcePortPool,
                outboundQueryOptions.CurrentValue.PoolSizePerAddressFamily,
                outboundQueryOptions.CurrentValue.ExcludedSourcePorts
            )
        );

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:OutboundQuery</c> via
    ///     <see cref="PresetConfigurationFileApplier" />'s own generic settings-list overload, RFC
    ///     7396 merge-patch style: a field absent from <paramref name="patch" /> is left exactly as
    ///     it was in the file.
    /// </summary>
    [HttpPatch]
    [RequirePermission("outboundquerysettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(OutboundQuerySettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];

        if (
            patch.TryGetMergePatchField(
                "enableSourcePortPool",
                out JsonElement enableSourcePortPool
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    OutboundQueryOptions.SectionName,
                    nameof(OutboundQueryOptions.EnableSourcePortPool),
                    enableSourcePortPool.GetBoolean()
                )
            );

        if (
            patch.TryGetMergePatchField(
                "poolSizePerAddressFamily",
                out JsonElement poolSizePerAddressFamily
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    OutboundQueryOptions.SectionName,
                    nameof(OutboundQueryOptions.PoolSizePerAddressFamily),
                    poolSizePerAddressFamily.GetInt32()
                )
            );

        if (patch.TryGetMergePatchField("excludedSourcePorts", out JsonElement excludedSourcePorts))
            settings.Add(
                new PresetConfigurationSetting(
                    OutboundQueryOptions.SectionName,
                    nameof(OutboundQueryOptions.ExcludedSourcePorts),
                    (List<int>)
                        [
                            .. excludedSourcePorts
                                .EnumerateArray()
                                .Select(element => element.GetInt32()),
                        ]
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
