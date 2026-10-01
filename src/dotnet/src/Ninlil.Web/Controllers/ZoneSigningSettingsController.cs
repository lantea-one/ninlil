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
///     This controller reads and writes <see cref="ZoneSigningOptions" />: the DNSSEC signature
///     validity, inception offset and re-signing timing that <c>ZoneSigningService</c> and
///     <c>DnssecResigningBackgroundService</c> use. Reading needs
///     <c>zonesigningsettings:read</c> and writing needs <c>zonesigningsettings:write</c>.
///     <para>
///         <b>Every field takes effect without a restart.</b> <c>ZoneSigningService</c> reads
///         <see cref="IOptionsMonitor{TOptions}" />'s <c>CurrentValue</c> on every signing call,
///         and <c>DnssecResigningBackgroundService</c> on every pass of its loop, so the next
///         signing or pass uses the new values.
///     </para>
/// </summary>
/// <param name="zoneSigningOptions">The current zone-signing timing.</param>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/zone-signing-settings")]
public class ZoneSigningSettingsController(
    IOptionsMonitor<ZoneSigningOptions> zoneSigningOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action returns the current zone-signing timing. Requires
    ///     <c>zonesigningsettings:read</c>.
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
    ///     This action merges a partial update into <c>DnsServer:Authoritative:ZoneSigning</c> as
    ///     an RFC 7396 merge patch, through <see cref="PresetConfigurationFileApplier" />. Every
    ///     field takes effect at once. Returns 400 for a malformed duration, a negative inception
    ///     offset, or a non-positive validity period, refresh margin or poll interval. Requires
    ///     <c>zonesigningsettings:write</c>.
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
