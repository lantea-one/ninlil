using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Authoritative.Options;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Examples.Settings;
using Ninlil.Web.Examples.ZoneTransferSettings;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller reads and writes this host's own <see cref="ZoneTransferOptions" /> -- how
///     this server chunks the AXFR/IXFR responses it serves out to a secondary --
///     `appsettings.json`-only until this pass. Every action requires the
///     <c>zonetransfersettings:read</c> or <c>zonetransfersettings:write</c> permission.
///     <para>
///         <b>Its one field takes effect immediately, with no restart.</b> <c>ZoneTransferService</c>
///         used to read <see cref="ZoneTransferOptions" /> via the snapshotted
///         <see cref="IOptions{TOptions}" />, cached for the life of the process the first time it
///         was read -- a real functional gap beyond REST/UI exposure, the same shape already
///         found and fixed elsewhere in this project for other options classes, converted here the same
///         way: <c>ZoneTransferService</c> now reads <see cref="IOptionsMonitor{TOptions}" />'s own
///         <c>CurrentValue</c> fresh on every single transfer it serves, even though a fresh
///         instance of that (scoped) service is itself constructed per transfer -- the plain,
///         snapshotted <see cref="IOptions{TOptions}" /> it used before this fix would still have
///         handed every one of those fresh instances back the exact same process-wide-cached
///         value.
///     </para>
/// </summary>
/// <param name="zoneTransferOptions">This host's own currently-effective zone-transfer-serving settings, live.</param>
/// <param name="runtimeConfigurationOptions">The JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/zone-transfer-settings")]
public class ZoneTransferSettingsController(
    IOptionsMonitor<ZoneTransferOptions> zoneTransferOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action reads this host's own currently-effective (live, even without a restart)
    ///     zone-transfer-serving settings.
    /// </summary>
    [HttpGet]
    [RequirePermission("zonetransfersettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ZoneTransferSettingsDetailExample))]
    public ActionResult<ZoneTransferSettingsDetail> Get() =>
        Ok(new ZoneTransferSettingsDetail(zoneTransferOptions.CurrentValue.RecordsPerMessage));

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:Authoritative:ZoneTransfer</c>
    ///     via <see cref="PresetConfigurationFileApplier" />'s own generic settings-list overload,
    ///     RFC 7396 merge-patch style. This options class's own single field hot-reloads
    ///     immediately -- see this class's own doc comment.
    /// </summary>
    [HttpPatch]
    [RequirePermission("zonetransfersettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(ZoneTransferSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];

        if (
            patch.TryGetMergePatchField(
                "recordsPerMessage",
                out JsonElement recordsPerMessageElement
            )
        )
        {
            int recordsPerMessage = recordsPerMessageElement.GetInt32();

            if (recordsPerMessage < 1)
                throw new ValidationException("'recordsPerMessage' must be at least 1.");

            settings.Add(
                new PresetConfigurationSetting(
                    ZoneTransferOptions.SectionName,
                    nameof(ZoneTransferOptions.RecordsPerMessage),
                    recordsPerMessage
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
