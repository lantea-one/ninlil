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

        if (patch.TryGetMergePatchField("primaryPort", out JsonElement primaryPortElement))
        {
            int primaryPort = primaryPortElement.GetInt32();

            if (primaryPort is < 1 or > 65535)
                throw new ValidationException("'primaryPort' must be between 1 and 65535.");

            settings.Add(
                new PresetConfigurationSetting(
                    ZoneTransferClientOptions.SectionName,
                    nameof(ZoneTransferClientOptions.PrimaryPort),
                    primaryPort
                )
            );
        }

        if (patch.TryGetMergePatchField("connectTimeout", out JsonElement connectTimeoutElement))
            settings.Add(
                new PresetConfigurationSetting(
                    ZoneTransferClientOptions.SectionName,
                    nameof(ZoneTransferClientOptions.ConnectTimeout),
                    ParsePositiveTimeSpan(connectTimeoutElement, "connectTimeout").ToString()
                )
            );

        if (patch.TryGetMergePatchField("readTimeout", out JsonElement readTimeoutElement))
            settings.Add(
                new PresetConfigurationSetting(
                    ZoneTransferClientOptions.SectionName,
                    nameof(ZoneTransferClientOptions.ReadTimeout),
                    ParsePositiveTimeSpan(readTimeoutElement, "readTimeout").ToString()
                )
            );

        if (patch.TryGetMergePatchField("soaQueryTimeout", out JsonElement soaQueryTimeoutElement))
            settings.Add(
                new PresetConfigurationSetting(
                    ZoneTransferClientOptions.SectionName,
                    nameof(ZoneTransferClientOptions.SoaQueryTimeout),
                    ParsePositiveTimeSpan(soaQueryTimeoutElement, "soaQueryTimeout").ToString()
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
    ///     This method parses a merge-patch field that must hold a positive TimeSpan, or throws a
    ///     <see cref="ValidationException" /> naming the field.
    /// </summary>
    private static TimeSpan ParsePositiveTimeSpan(JsonElement element, string fieldName)
    {
        if (!TimeSpan.TryParse(element.GetString(), out TimeSpan parsed) || parsed <= TimeSpan.Zero)
            throw new ValidationException(
                $"'{fieldName}' must be a valid, positive TimeSpan (for example \"00:00:03\")."
            );

        return parsed;
    }
}
