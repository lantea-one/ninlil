using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Dnssec;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Examples.DnssecPolicySettings;
using Ninlil.Web.Examples.Settings;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller reads and writes this host's own <see cref="DnssecAlgorithmPolicyOptions" />
///     -- which DNSSEC algorithms a brand-new signing key may use -- `appsettings.json`-only until
///     this pass, despite already being a real, enforced restriction
///     <c>DnssecKeyManagementService.GenerateKeyAsync</c> consults on every key-generation call
///     (most notably as the mechanism the <c>FIPS</c> preset relies on). Every action requires the
///     <c>dnssecpolicysettings:read</c> or <c>dnssecpolicysettings:write</c> permission.
///     <para>
///         <b>Every field takes effect immediately, with no restart.</b>
///         <c>DnssecKeyManagementService</c> used to read <see cref="DnssecAlgorithmPolicyOptions" />
///         via the snapshotted <see cref="IOptions{TOptions}" />, cached for the life of the
///         process the first time it was read -- a real functional gap beyond REST/UI exposure,
///         the same live-reload gap already found and fixed elsewhere in this project for other
///         options classes, converted here the same way: <c>DnssecKeyManagementService</c> now reads
///         <see cref="IOptionsMonitor{TOptions}" />'s own <c>CurrentValue</c> fresh on every
///         <c>GenerateKeyAsync</c> call.
///     </para>
/// </summary>
/// <param name="algorithmPolicyOptions">This host's own currently-effective DNSSEC algorithm policy, live.</param>
/// <param name="runtimeConfigurationOptions">The JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/dnssec-policy-settings")]
public class DnssecPolicySettingsController(
    IOptionsMonitor<DnssecAlgorithmPolicyOptions> algorithmPolicyOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action reads this host's own currently-effective (live, even without a restart)
    ///     DNSSEC algorithm policy.
    /// </summary>
    [HttpGet]
    [RequirePermission("dnssecpolicysettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(DnssecPolicySettingsDetailExample))]
    public ActionResult<DnssecPolicySettingsDetail> Get() =>
        Ok(new DnssecPolicySettingsDetail(algorithmPolicyOptions.CurrentValue.AllowedAlgorithms));

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:Dnssec</c> via
    ///     <see cref="PresetConfigurationFileApplier" />'s own generic settings-list overload, RFC
    ///     7396 merge-patch style. Every field hot-reloads immediately -- see this class's own doc
    ///     comment.
    /// </summary>
    [HttpPatch]
    [RequirePermission("dnssecpolicysettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(DnssecPolicySettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];

        if (
            patch.TryGetMergePatchField(
                "allowedAlgorithms",
                out JsonElement allowedAlgorithmsElement
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    DnssecAlgorithmPolicyOptions.SectionName,
                    nameof(DnssecAlgorithmPolicyOptions.AllowedAlgorithms),
                    ParseAllowedAlgorithms(allowedAlgorithmsElement)
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
    ///     This method parses an <c>allowedAlgorithms</c> merge-patch field (a JSON array of
    ///     DNSSEC algorithm names, in the exact same wire format <c>DnssecKeysController</c>'s own
    ///     <c>algorithm</c> field already round-trips through, rather than a raw C# enum member
    ///     name) into the validated name list <see cref="DnssecAlgorithmPolicyOptions.AllowedAlgorithms" />
    ///     binds from.
    /// </summary>
    private static List<string> ParseAllowedAlgorithms(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
            throw new ValidationException(
                "'allowedAlgorithms' must be a JSON array of DNSSEC algorithm names (for "
                    + "example [\"ECDSAP256SHA256\", \"ED25519\"]), or an empty array to allow "
                    + "every algorithm this server supports."
            );

        List<string> parsed = [];

        foreach (JsonElement item in element.EnumerateArray())
        {
            DnssecAlgorithmEnum algorithm;

            try
            {
                algorithm = JsonSerializer.Deserialize<DnssecAlgorithmEnum>(item.GetRawText());
            }
            catch (JsonException)
            {
                throw new ValidationException($"'{item}' is not a valid DNSSEC algorithm name.");
            }

            // JsonSerializer.Deserialize<TEnum> for this project's own EnumDescriptionJsonConverter
            // silently falls back to the enum's default (unnamed, zero) value for an unmatched
            // string rather than throwing -- confirmed directly against a real failing test, not
            // assumed -- so this project's own JsonException catch above alone would never
            // actually reject a bad name; Enum.IsDefined closes that gap explicitly.
            if (!Enum.IsDefined(algorithm))
                throw new ValidationException($"'{item}' is not a valid DNSSEC algorithm name.");

            parsed.Add(algorithm.ToString());
        }

        return parsed;
    }
}
