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
///     This controller reads and writes <see cref="ZoneTransferOptions" />: how many records this
///     server puts in each message of the AXFR/IXFR responses it serves to secondaries. Reading
///     needs <c>zonetransfersettings:read</c> and writing needs
///     <c>zonetransfersettings:write</c>.
///     <para>
///         <b>The field takes effect at once, with no restart.</b> <c>ZoneTransferService</c>
///         reads <see cref="IOptionsMonitor{TOptions}" />'s <c>CurrentValue</c> for every transfer
///         it serves.
///     </para>
/// </summary>
/// <param name="zoneTransferOptions">The current zone-transfer serving settings.</param>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/zone-transfer-settings")]
public class ZoneTransferSettingsController(
    IOptionsMonitor<ZoneTransferOptions> zoneTransferOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action returns the current zone-transfer serving settings. Requires
    ///     <c>zonetransfersettings:read</c>.
    /// </summary>
    [HttpGet]
    [RequirePermission("zonetransfersettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ZoneTransferSettingsDetailExample))]
    public ActionResult<ZoneTransferSettingsDetail> Get() =>
        Ok(new ZoneTransferSettingsDetail(zoneTransferOptions.CurrentValue.RecordsPerMessage));

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:Authoritative:ZoneTransfer</c> as
    ///     an RFC 7396 merge patch, through <see cref="PresetConfigurationFileApplier" />. The
    ///     change takes effect at once. Returns 400 when <c>recordsPerMessage</c> is below 1.
    ///     Requires <c>zonetransfersettings:write</c>.
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
