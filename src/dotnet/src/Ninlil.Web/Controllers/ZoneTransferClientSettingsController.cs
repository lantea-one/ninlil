using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Examples.Settings;
using Ninlil.Web.Examples.ZoneTransferClientSettings;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller reads and writes <see cref="ZoneTransferClientOptions" />: the port and
///     timeouts this server uses as a secondary when it pulls AXFR/IXFR transfers from a primary.
///     Reading needs <c>zonetransferclientsettings:read</c> and writing needs
///     <c>zonetransferclientsettings:write</c>.
///     <para>
///         <b>Every field takes effect at once, with no restart.</b>
///         <c>TcpZoneTransferClient</c> reads <see cref="IOptionsMonitor{TOptions}" />'s
///         <c>CurrentValue</c> on every pull.
///     </para>
/// </summary>
/// <param name="zoneTransferClientOptions">The current zone-transfer pulling settings.</param>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/zone-transfer-client-settings")]
public class ZoneTransferClientSettingsController(
    IOptionsMonitor<ZoneTransferClientOptions> zoneTransferClientOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action returns the current zone-transfer pulling settings. Requires
    ///     <c>zonetransferclientsettings:read</c>.
    /// </summary>
    [HttpGet]
    [RequirePermission("zonetransferclientsettings:read")]
    [SwaggerResponseExample(
        StatusCodes.Status200OK,
        typeof(ZoneTransferClientSettingsDetailExample)
    )]
    public ActionResult<ZoneTransferClientSettingsDetail> Get() =>
        Ok(
            new ZoneTransferClientSettingsDetail(
                zoneTransferClientOptions.CurrentValue.PrimaryPort,
                zoneTransferClientOptions.CurrentValue.ConnectTimeout,
                zoneTransferClientOptions.CurrentValue.ReadTimeout,
                zoneTransferClientOptions.CurrentValue.SoaQueryTimeout
            )
        );

    /// <summary>
    ///     This action merges a partial update into
    ///     <c>DnsServer:Authoritative:ZoneTransferClient</c> as an RFC 7396 merge patch, through
    ///     <see cref="PresetConfigurationFileApplier" />. Every field takes effect at once. Returns
    ///     400 for a port outside 1-65535 or a timeout that is not a positive TimeSpan. Requires
    ///     <c>zonetransferclientsettings:write</c>.
    /// </summary>
    [HttpPatch]
    [RequirePermission("zonetransferclientsettings:write")]
    [SwaggerRequestExample(
        typeof(JsonElement),
        typeof(ZoneTransferClientSettingsPatchRequestExample)
    )]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];

        if (patch.TryGetMergePatchInt32("primaryPort", out int primaryPort, 1, 65535))
            settings.Add(
                new PresetConfigurationSetting(
                    ZoneTransferClientOptions.SectionName,
                    nameof(ZoneTransferClientOptions.PrimaryPort),
                    primaryPort
                )
            );

        // The timeouts feed CancelAfter and socket waits, so they are capped at the timer limit.
        if (
            patch.TryGetMergePatchDuration(
                "connectTimeout",
                out TimeSpan connectTimeout,
                TimeSpan.FromMilliseconds(1),
                JsonElementMergePatchExtension.TimerMaximum
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    ZoneTransferClientOptions.SectionName,
                    nameof(ZoneTransferClientOptions.ConnectTimeout),
                    connectTimeout.ToString()
                )
            );

        if (
            patch.TryGetMergePatchDuration(
                "readTimeout",
                out TimeSpan readTimeout,
                TimeSpan.FromMilliseconds(1),
                JsonElementMergePatchExtension.TimerMaximum
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    ZoneTransferClientOptions.SectionName,
                    nameof(ZoneTransferClientOptions.ReadTimeout),
                    readTimeout.ToString()
                )
            );

        if (
            patch.TryGetMergePatchDuration(
                "soaQueryTimeout",
                out TimeSpan soaQueryTimeout,
                TimeSpan.FromMilliseconds(1),
                JsonElementMergePatchExtension.TimerMaximum
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    ZoneTransferClientOptions.SectionName,
                    nameof(ZoneTransferClientOptions.SoaQueryTimeout),
                    soaQueryTimeout.ToString()
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
