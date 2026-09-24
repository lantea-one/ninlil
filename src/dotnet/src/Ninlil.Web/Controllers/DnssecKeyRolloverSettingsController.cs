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
///     This controller reads and writes this host's own <see cref="DnssecKeyRolloverOptions" />
///     -- the RFC 6781 §4.1.1.1 (Pre-Publish, ZSK)/§4.1.2 (Double-Signature, KSK) key-rollover
///     timing <see cref="DnssecKeyRolloverBackgroundService" /> walks every locally signed zone's
///     own keys against -- `appsettings.json`-only until this pass. An operator genuinely needs
///     <see cref="DnssecKeyRolloverOptions.ExternalParentDsTtl" /> configurable at runtime: it is
///     this project's own stand-in for a real external registrar's own DS record TTL (which this
///     project has no live way to observe), and getting it wrong either withdraws a retired KSK
///     too early (a real validation failure for a resolver still holding the old DS in cache) or
///     needlessly delays withdrawal. Every action requires the
///     <c>dnsseckeyrolloversettings:read</c> or <c>dnsseckeyrolloversettings:write</c> permission.
///     <para>
///         <b>Every field takes effect immediately, with no restart.</b>
///         <see cref="DnssecKeyRolloverBackgroundService" /> already reads
///         <see cref="IOptionsMonitor{TOptions}" />'s own <c>CurrentValue</c> fresh on every
///         zone it processes within a single poll pass, and reads
///         <see cref="DnssecKeyRolloverOptions.PollInterval" /> fresh for its own next
///         <see cref="Task.Delay(TimeSpan, CancellationToken)" /> wait too -- a write this
///         controller's own <see cref="Patch" /> merges into the configuration file changes what
///         the very next rollover check or poll wait uses, no restart, no new instance.
///     </para>
/// </summary>
/// <param name="rolloverOptions">This host's own currently-effective key-rollover timing, live.</param>
/// <param name="runtimeConfigurationOptions">The JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/dnssec-key-rollover-settings")]
public class DnssecKeyRolloverSettingsController(
    IOptionsMonitor<DnssecKeyRolloverOptions> rolloverOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action reads this host's own currently-effective (live, even without a restart)
    ///     DNSSEC key-rollover timing configuration.
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
    ///     <c>DnsServer:Authoritative:DnssecKeyRollover</c> via
    ///     <see cref="PresetConfigurationFileApplier" />'s own generic settings-list overload, RFC
    ///     7396 merge-patch style. Every field hot-reloads immediately -- see this class's own
    ///     doc comment.
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
    ///     This method reads a merge-patch field expected to hold a <see cref="TimeSpan" />-parseable
    ///     string, distinguishing "absent" from "present but unparseable" for the caller -- the
    ///     same helper shape <see cref="ZoneSigningSettingsController" />'s own equivalent already
    ///     establishes.
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
