using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Authoritative.Options;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Examples.PtrSyncSettings;
using Ninlil.Web.Examples.Settings;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller reads and writes <see cref="PtrSyncOptions" />: the server-wide defaults
///     for automatic PTR (reverse-zone) sync, which a zone can override through
///     <c>ZoneEntity.AutoManagePtrRecords</c> and its prefix lengths on <c>ZonesController</c>.
///     Reading needs <c>ptrsyncsettings:read</c> and writing needs <c>ptrsyncsettings:write</c>.
///     <para>
///         <b>Every field takes effect at once, with no restart.</b> <c>PtrSyncService</c> reads
///         <see cref="IOptionsMonitor{TOptions}" />'s <c>CurrentValue</c> on every sync, and each
///         sync is triggered by a <c>DnsZoneChangedEvent</c> or an on-demand request.
///     </para>
/// </summary>
/// <param name="ptrSyncOptions">The current PTR sync defaults.</param>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/ptr-sync-settings")]
public class PtrSyncSettingsController(
    IOptionsMonitor<PtrSyncOptions> ptrSyncOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action returns the current PTR sync defaults. Requires <c>ptrsyncsettings:read</c>.
    /// </summary>
    [HttpGet]
    [RequirePermission("ptrsyncsettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(PtrSyncSettingsDetailExample))]
    public ActionResult<PtrSyncSettingsDetail> Get() =>
        Ok(
            new PtrSyncSettingsDetail(
                ptrSyncOptions.CurrentValue.DefaultEnabled,
                ptrSyncOptions.CurrentValue.DefaultIpv4ReverseZonePrefixBits,
                ptrSyncOptions.CurrentValue.DefaultIpv6ReverseZonePrefixBits
            )
        );

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:Authoritative:PtrSync</c> as an
    ///     RFC 7396 merge patch, through <see cref="PresetConfigurationFileApplier" />. Every field
    ///     takes effect at once. Returns 400 for an IPv4 prefix outside 24-30 or an IPv6 prefix
    ///     that is not a multiple of 4 from 16 to 124 (see <c>ZonesController</c> for why).
    ///     Requires <c>ptrsyncsettings:write</c>.
    /// </summary>
    [HttpPatch]
    [RequirePermission("ptrsyncsettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(PtrSyncSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];

        if (patch.TryGetMergePatchField("defaultEnabled", out JsonElement defaultEnabled))
            settings.Add(
                new PresetConfigurationSetting(
                    PtrSyncOptions.SectionName,
                    nameof(PtrSyncOptions.DefaultEnabled),
                    defaultEnabled.GetBoolean()
                )
            );

        if (
            patch.TryGetMergePatchField(
                "defaultIpv4ReverseZonePrefixBits",
                out JsonElement defaultIpv4ReverseZonePrefixBits
            )
        )
        {
            int value = defaultIpv4ReverseZonePrefixBits.GetInt32();
            if (value < 24 || value > 30)
                throw new ValidationException(
                    $"'{value}' is not a valid default IPv4 prefix length -- it must be between 24 and 30 inclusive."
                );

            settings.Add(
                new PresetConfigurationSetting(
                    PtrSyncOptions.SectionName,
                    nameof(PtrSyncOptions.DefaultIpv4ReverseZonePrefixBits),
                    value
                )
            );
        }

        if (
            patch.TryGetMergePatchField(
                "defaultIpv6ReverseZonePrefixBits",
                out JsonElement defaultIpv6ReverseZonePrefixBits
            )
        )
        {
            int value = defaultIpv6ReverseZonePrefixBits.GetInt32();
            if (value % 4 != 0 || value < 16 || value > 124)
                throw new ValidationException(
                    $"'{value}' is not a valid default IPv6 prefix length -- it must be a multiple of 4 between 16 and 124 inclusive."
                );

            settings.Add(
                new PresetConfigurationSetting(
                    PtrSyncOptions.SectionName,
                    nameof(PtrSyncOptions.DefaultIpv6ReverseZonePrefixBits),
                    value
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
