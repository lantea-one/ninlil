using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Exceptions;
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

        if (patch.TryGetMergePatchBoolean("enableSourcePortPool", out bool enableSourcePortPool))
            settings.Add(
                new PresetConfigurationSetting(
                    OutboundQueryOptions.SectionName,
                    nameof(OutboundQueryOptions.EnableSourcePortPool),
                    enableSourcePortPool
                )
            );

        if (
            patch.TryGetMergePatchInt32(
                "poolSizePerAddressFamily",
                out int poolSizePerAddressFamily,
                1,
                65535
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    OutboundQueryOptions.SectionName,
                    nameof(OutboundQueryOptions.PoolSizePerAddressFamily),
                    poolSizePerAddressFamily
                )
            );

        if (patch.TryGetMergePatchField("excludedSourcePorts", out JsonElement excludedSourcePorts))
            settings.Add(
                new PresetConfigurationSetting(
                    OutboundQueryOptions.SectionName,
                    nameof(OutboundQueryOptions.ExcludedSourcePorts),
                    ReadPortList(excludedSourcePorts)
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
    ///     This method reads <c>excludedSourcePorts</c>: a JSON array of whole numbers from 1 to
    ///     65535. Anything else is a 400 that names the field.
    /// </summary>
    private static List<int> ReadPortList(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
            throw new ValidationException(
                "'excludedSourcePorts' must be an array of port numbers."
            );

        List<int> ports = [];
        foreach (JsonElement item in element.EnumerateArray())
        {
            if (
                item.ValueKind != JsonValueKind.Number
                || !item.TryGetInt32(out int port)
                || port is < 1 or > 65535
            )
                throw new ValidationException(
                    "'excludedSourcePorts' must contain only whole numbers between 1 and 65535."
                );

            ports.Add(port);
        }

        return ports;
    }
}
