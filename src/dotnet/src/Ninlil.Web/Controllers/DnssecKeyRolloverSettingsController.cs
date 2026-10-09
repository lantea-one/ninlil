using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Authoritative.Dnssec;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Examples.DnssecKeyRolloverSettings;
using Ninlil.Web.Examples.Settings;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller reads and writes <see cref="DnssecKeyRolloverOptions" />: the timing that
///     <see cref="DnssecKeyRolloverBackgroundService" /> applies when it walks each locally signed
///     zone's keys through an RFC 6781 rollover (Pre-Publish for a ZSK, §4.1.1.1; Double-Signature
///     for a KSK, §4.1.2). <see cref="DnssecKeyRolloverOptions.ExternalParentDsTtl" /> matters
///     most: it stands in for the DS record TTL at an external registrar, which the server cannot
///     observe. Too short withdraws a retired KSK while resolvers may still cache the old DS, so
///     validation fails; too long only delays the withdrawal. Reading needs
///     <c>dnsseckeyrolloversettings:read</c> and writing needs
///     <c>dnsseckeyrolloversettings:write</c>.
///     <para>
///         <b>Every field takes effect without a restart.</b>
///         <see cref="DnssecKeyRolloverBackgroundService" /> reads
///         <see cref="IOptionsMonitor{TOptions}" />'s <c>CurrentValue</c> for each zone it
///         processes and for each wait between passes, so the next check or wait uses the new
///         values.
///     </para>
/// </summary>
/// <param name="rolloverOptions">The current key-rollover timing.</param>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/dnssec-key-rollover-settings")]
public class DnssecKeyRolloverSettingsController(
    IOptionsMonitor<DnssecKeyRolloverOptions> rolloverOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action returns the current DNSSEC key-rollover timing. Requires
    ///     <c>dnsseckeyrolloversettings:read</c>.
    /// </summary>
    [HttpGet]
    [RequirePermission("dnsseckeyrolloversettings:read")]
    [SwaggerResponseExample(
        StatusCodes.Status200OK,
        typeof(DnssecKeyRolloverSettingsDetailExample)
    )]
    public ActionResult<DnssecKeyRolloverSettingsDetail> Get()
    {
        DnssecKeyRolloverOptions options = rolloverOptions.CurrentValue;

        return Ok(
            new DnssecKeyRolloverSettingsDetail(
                options.PropagationDelay,
                options.ExternalParentDsTtl,
                options.PollInterval
            )
        );
    }

    /// <summary>
    ///     This action merges a partial update into
    ///     <c>DnsServer:Authoritative:DnssecKeyRollover</c> as an RFC 7396 merge patch, through
    ///     <see cref="PresetConfigurationFileApplier" />. Every field takes effect at once. Returns
    ///     400 for a malformed duration, a negative propagation delay, or a non-positive parent DS
    ///     TTL or poll interval. Requires <c>dnsseckeyrolloversettings:write</c>.
    /// </summary>
    [HttpPatch]
    [RequirePermission("dnsseckeyrolloversettings:write")]
    [SwaggerRequestExample(
        typeof(JsonElement),
        typeof(DnssecKeyRolloverSettingsPatchRequestExample)
    )]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];

        // Zero is a valid propagation delay (no wait). Both TTL-like values are added to
        // timestamps, so the upper bound keeps the sums inside DateTimeOffset.
        if (
            patch.TryGetMergePatchDuration(
                "propagationDelay",
                out TimeSpan propagationDelay,
                TimeSpan.Zero,
                TimeSpan.FromDays(36500)
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    DnssecKeyRolloverOptions.SectionName,
                    nameof(DnssecKeyRolloverOptions.PropagationDelay),
                    propagationDelay.ToString()
                )
            );

        if (
            patch.TryGetMergePatchDuration(
                "externalParentDsTtl",
                out TimeSpan externalParentDsTtl,
                TimeSpan.FromMilliseconds(1),
                TimeSpan.FromDays(36500)
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    DnssecKeyRolloverOptions.SectionName,
                    nameof(DnssecKeyRolloverOptions.ExternalParentDsTtl),
                    externalParentDsTtl.ToString()
                )
            );

        // The poll interval feeds a timer.
        if (
            patch.TryGetMergePatchDuration(
                "pollInterval",
                out TimeSpan pollInterval,
                TimeSpan.FromMilliseconds(1),
                JsonElementMergePatchExtension.TimerMaximum
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    DnssecKeyRolloverOptions.SectionName,
                    nameof(DnssecKeyRolloverOptions.PollInterval),
                    pollInterval.ToString()
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
