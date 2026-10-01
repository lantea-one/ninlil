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
///     This controller reads and writes <see cref="OutboundQueryOptions" />: whether outbound
///     UDP queries use a randomized source-port pool, its size per address family, and ports to
///     avoid. Reading needs <c>outboundquerysettings:read</c> and writing needs
///     <c>outboundquerysettings:write</c>.
///     <para>
///         Every field takes effect without a restart: <c>OutboundUdpSocketPool</c> reads
///         <see cref="OutboundQueryOptions" /> through <see cref="IOptionsMonitor{TOptions}" />.
///     </para>
/// </summary>
/// <param name="outboundQueryOptions">The current outbound-query settings.</param>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/outbound-query-settings")]
public class OutboundQuerySettingsController(
    IOptionsMonitor<OutboundQueryOptions> outboundQueryOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action returns the current outbound-query settings. Requires
    ///     <c>outboundquerysettings:read</c>.
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
    ///     This action merges a partial update into <c>DnsServer:OutboundQuery</c> as an RFC 7396
    ///     merge patch, through <see cref="PresetConfigurationFileApplier" />. A field absent from
    ///     <paramref name="patch" /> keeps its value; <c>excludedSourcePorts</c> replaces the whole
    ///     list. No range checks are applied. Requires <c>outboundquerysettings:write</c>.
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
