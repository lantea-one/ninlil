using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Authoritative.Dnssec;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Examples.Settings;
using Ninlil.Web.Examples.ZoneSigningSettings;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller reads and writes this host's own <see cref="ZoneSigningOptions" /> -- the
///     DNSSEC signature validity/inception/refresh timing <c>ZoneSigningService</c>/
///     <c>DnssecResigningBackgroundService</c> both consult -- `appsettings.json`-only until this
///     pass. Every action requires the <c>zonesigningsettings:read</c> or
///     <c>zonesigningsettings:write</c> permission.
///     <para>
///         <b>Every field takes effect immediately, with no restart.</b> Both consuming classes
///         used to read <see cref="ZoneSigningOptions" /> via the snapshotted
///         <see cref="IOptions{TOptions}" />, cached for the life of the process the first time it
///         was read -- a real functional gap beyond REST/UI exposure, the same shape already
///         found and fixed elsewhere in this project for other options classes, converted here the same
///         way: <c>ZoneSigningService</c> reads <see cref="IOptionsMonitor{TOptions}" />'s own
///         <c>CurrentValue</c> fresh on every signing call, and <c>DnssecResigningBackgroundService</c>
///         reads it fresh on every iteration of its own polling loop -- a write this controller's
///         own <see cref="Patch" /> merges into the configuration file changes what the *very next*
///         sign or poll iteration uses, whichever comes first, no restart, no new instance.
///     </para>
/// </summary>
/// <param name="zoneSigningOptions">This host's own currently-effective zone-signing timing, live.</param>
/// <param name="runtimeConfigurationOptions">The JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/zone-signing-settings")]
public class ZoneSigningSettingsController(
    IOptionsMonitor<ZoneSigningOptions> zoneSigningOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action reads this host's own currently-effective (live, even without a restart)
    ///     zone-signing timing configuration.
    /// </summary>
    [HttpGet]
    [RequirePermission("zonesigningsettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ZoneSigningSettingsDetailExample))]
    public ActionResult<ZoneSigningSettingsDetail> Get()
    {
        ZoneSigningOptions options = zoneSigningOptions.CurrentValue;

        return Ok(
            new ZoneSigningSettingsDetail(
                options.SignatureValidityPeriod,
                options.InceptionOffset,
                options.RefreshBeforeExpiration,
                options.PollInterval
            )
        );
    }

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:Authoritative:ZoneSigning</c> via
    ///     <see cref="PresetConfigurationFileApplier" />'s own generic settings-list overload, RFC
    ///     7396 merge-patch style. Every field hot-reloads immediately -- see this class's own doc
    ///     comment.
    /// </summary>
    [HttpPatch]
    [RequirePermission("zonesigningsettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(ZoneSigningSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];

        if (
            TryGetDuration(
                patch,
                "signatureValidityPeriod",
                out TimeSpan signatureValidityPeriod,
                out bool signatureValidityPeriodPresent
            )
        )
        {
            if (signatureValidityPeriod <= TimeSpan.Zero)
                throw new ValidationException("'signatureValidityPeriod' must be positive.");

            settings.Add(
                new PresetConfigurationSetting(
                    ZoneSigningOptions.SectionName,
                    nameof(ZoneSigningOptions.SignatureValidityPeriod),
                    signatureValidityPeriod.ToString()
                )
            );
        }
        else if (signatureValidityPeriodPresent)
            throw new ValidationException(
                "'signatureValidityPeriod' must be a valid TimeSpan (for example \"30.00:00:00\")."
            );

        if (
            TryGetDuration(
                patch,
                "inceptionOffset",
                out TimeSpan inceptionOffset,
                out bool inceptionOffsetPresent
            )
        )
        {
            if (inceptionOffset < TimeSpan.Zero)
                throw new ValidationException("'inceptionOffset' must not be negative.");

            settings.Add(
                new PresetConfigurationSetting(
                    ZoneSigningOptions.SectionName,
                    nameof(ZoneSigningOptions.InceptionOffset),
                    inceptionOffset.ToString()
                )
            );
        }
        else if (inceptionOffsetPresent)
            throw new ValidationException(
                "'inceptionOffset' must be a valid TimeSpan (for example \"01:00:00\")."
            );

        if (
            TryGetDuration(
                patch,
                "refreshBeforeExpiration",
                out TimeSpan refreshBeforeExpiration,
                out bool refreshBeforeExpirationPresent
            )
        )
        {
            if (refreshBeforeExpiration <= TimeSpan.Zero)
                throw new ValidationException("'refreshBeforeExpiration' must be positive.");

            settings.Add(
                new PresetConfigurationSetting(
                    ZoneSigningOptions.SectionName,
                    nameof(ZoneSigningOptions.RefreshBeforeExpiration),
                    refreshBeforeExpiration.ToString()
                )
            );
        }
        else if (refreshBeforeExpirationPresent)
            throw new ValidationException(
                "'refreshBeforeExpiration' must be a valid TimeSpan (for example \"5.00:00:00\")."
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
                    ZoneSigningOptions.SectionName,
                    nameof(ZoneSigningOptions.PollInterval),
                    pollInterval.ToString()
                )
            );
        }
        else if (pollIntervalPresent)
            throw new ValidationException(
                "'pollInterval' must be a valid TimeSpan (for example \"06:00:00\")."
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
    ///     same helper <see cref="RecursiveSettingsController" />/<see cref="TransportSettingsController" />
    ///     each already establish independently.
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
