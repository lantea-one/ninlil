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

        if (
            TryGetDuration(
                patch,
                "propagationDelay",
                out TimeSpan propagationDelay,
                out bool propagationDelayPresent
            )
        )
        {
            if (propagationDelay < TimeSpan.Zero)
                throw new ValidationException("'propagationDelay' must not be negative.");

            settings.Add(
                new PresetConfigurationSetting(
                    DnssecKeyRolloverOptions.SectionName,
                    nameof(DnssecKeyRolloverOptions.PropagationDelay),
                    propagationDelay.ToString()
                )
            );
        }
        else if (propagationDelayPresent)
            throw new ValidationException(
                "'propagationDelay' must be a valid TimeSpan (for example \"01:00:00\")."
            );

        if (
            TryGetDuration(
                patch,
                "externalParentDsTtl",
                out TimeSpan externalParentDsTtl,
                out bool externalParentDsTtlPresent
            )
        )
        {
            if (externalParentDsTtl <= TimeSpan.Zero)
                throw new ValidationException("'externalParentDsTtl' must be positive.");

            settings.Add(
                new PresetConfigurationSetting(
                    DnssecKeyRolloverOptions.SectionName,
                    nameof(DnssecKeyRolloverOptions.ExternalParentDsTtl),
                    externalParentDsTtl.ToString()
                )
            );
        }
        else if (externalParentDsTtlPresent)
            throw new ValidationException(
                "'externalParentDsTtl' must be a valid TimeSpan (for example \"1.00:00:00\")."
            );

        if (
            TryGetDuration(
                patch,
                "pollInterval",
                out TimeSpan pollInterval,
                out bool pollIntervalPresent
            )
        )
        {
            if (pollInterval <= TimeSpan.Zero)
                throw new ValidationException("'pollInterval' must be positive.");

            settings.Add(
                new PresetConfigurationSetting(
                    DnssecKeyRolloverOptions.SectionName,
                    nameof(DnssecKeyRolloverOptions.PollInterval),
                    pollInterval.ToString()
                )
            );
        }
        else if (pollIntervalPresent)
            throw new ValidationException(
                "'pollInterval' must be a valid TimeSpan (for example \"00:15:00\")."
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
    ///     This method reads a merge-patch field that should hold a TimeSpan string, telling the
    ///     caller both whether the field was present and whether it parsed.
    /// </summary>
    private static bool TryGetDuration(
        JsonElement patch,
        string propertyName,
        out TimeSpan value,
        out bool present
    )
    {
        value = default;
        present = patch.TryGetMergePatchField(propertyName, out JsonElement element);

        return present && TimeSpan.TryParse(element.GetString(), out value);
    }
}
