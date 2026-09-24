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
///     This controller reads and writes this host's own <see cref="ZoneTransferClientOptions" />
///     -- the port and timeouts this server uses while pulling an AXFR/IXFR transfer from a
///     primary as a secondary -- `appsettings.json`-only until this pass. Every action requires
///     the <c>zonetransferclientsettings:read</c> or <c>zonetransferclientsettings:write</c>
///     permission.
///     <para>
///         <b>Every field takes effect immediately, with no restart.</b> <c>TcpZoneTransferClient</c>
///         used to read <see cref="ZoneTransferClientOptions" /> via the snapshotted
///         <see cref="IOptions{TOptions}" />, cached for the life of the process the first time it
///         was read -- a real functional gap beyond REST/UI exposure, the same shape already
///         found and fixed elsewhere in this project for other options classes, converted here the same
///         way: <c>TcpZoneTransferClient</c> now reads <see cref="IOptionsMonitor{TOptions}" />'s
///         own <c>CurrentValue</c> fresh on every single pull it attempts, even though a fresh
///         instance of that (scoped) client is itself constructed per pull -- the plain,
///         snapshotted <see cref="IOptions{TOptions}" /> it used before this fix would still have
///         handed every one of those fresh instances back the exact same process-wide-cached
///         value.
///     </para>
/// </summary>
/// <param name="zoneTransferClientOptions">This host's own currently-effective zone-transfer-pulling settings, live.</param>
/// <param name="runtimeConfigurationOptions">The JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/zone-transfer-client-settings")]
public class ZoneTransferClientSettingsController(
    IOptionsMonitor<ZoneTransferClientOptions> zoneTransferClientOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action reads this host's own currently-effective (live, even without a restart)
    ///     zone-transfer-pulling settings.
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
    ///     <c>DnsServer:Authoritative:ZoneTransferClient</c> via
    ///     <see cref="PresetConfigurationFileApplier" />'s own generic settings-list overload, RFC
    ///     7396 merge-patch style. Every field this options class exposes hot-reloads immediately
    ///     -- see this class's own doc comment.
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
    ///     This method parses a merge-patch field expected to be a positive <see cref="TimeSpan" />,
    ///     matching this project's own established convention of a small, controller-local helper
    ///     over a shared one (see <c>TryGetDuration</c>'s own several independent copies already
    ///     in this codebase).
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
