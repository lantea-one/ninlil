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
///     This controller reads and writes this host's own <see cref="PtrSyncOptions" /> -- the
///     global default for automatic PTR/reverse-zone sync -- `appsettings.json`-only until this
///     pass, despite the per-zone override (<c>ZoneEntity.AutoManagePtrRecords</c>) already having
///     a real REST/UI surface of its own through <c>ZonesController</c>. Every action requires the
///     <c>ptrsyncsettings:read</c> or <c>ptrsyncsettings:write</c> permission.
///     <para>
///         <b>Every field takes effect immediately, with no restart.</b> <c>PtrSyncService</c>
///         used to read <see cref="PtrSyncOptions" /> via the snapshotted <see cref="IOptions{TOptions}" />,
///         cached for the life of the process the first time it was read -- a real functional gap
///         beyond REST/UI exposure, the same shape already found and fixed elsewhere in this
///         project for other options classes, converted here the same way: <c>PtrSyncService</c> now
///         reads <see cref="IOptionsMonitor{TOptions}" />'s own <c>CurrentValue</c> fresh on every
///         sync call, itself triggered off the back of <c>DnsZoneChangedEvent</c> rather than any
///         bound socket or long-lived loop, so there is no "read once at StartAsync" concern here
///         at all.
///     </para>
/// </summary>
/// <param name="ptrSyncOptions">This host's own currently-effective PTR sync default, live.</param>
/// <param name="runtimeConfigurationOptions">The JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/ptr-sync-settings")]
public class PtrSyncSettingsController(
    IOptionsMonitor<PtrSyncOptions> ptrSyncOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action reads this host's own currently-effective (live, even without a restart)
    ///     PTR sync default.
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
    ///     This action merges a partial update into <c>DnsServer:Authoritative:PtrSync</c> via
    ///     <see cref="PresetConfigurationFileApplier" />'s own generic settings-list overload, RFC
    ///     7396 merge-patch style. The single field this options class exposes hot-reloads
    ///     immediately -- see this class's own doc comment.
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
