using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Dnssec;
using Ninlil.Dns.Server.Dnssec.Algorithms;
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
///     This controller reads and writes <see cref="DnssecAlgorithmPolicyOptions" />: which DNSSEC
///     algorithms a new signing key may use. <c>DnssecKeyManagementService.GenerateKeyAsync</c>
///     enforces it on every key it generates, and the <c>FIPS</c> preset relies on it. Reading
///     needs <c>dnssecpolicysettings:read</c> and writing needs
///     <c>dnssecpolicysettings:write</c>.
///     <para>
///         <b>Every field takes effect at once, with no restart.</b>
///         <c>DnssecKeyManagementService</c> reads <see cref="IOptionsMonitor{TOptions}" />'s
///         <c>CurrentValue</c> on every <c>GenerateKeyAsync</c> call. Existing keys are not
///         affected.
///     </para>
/// </summary>
/// <param name="algorithmPolicyOptions">The current DNSSEC algorithm policy.</param>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/dnssec-policy-settings")]
public class DnssecPolicySettingsController(
    IOptionsMonitor<DnssecAlgorithmPolicyOptions> algorithmPolicyOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action returns the current DNSSEC algorithm policy; an empty list allows every
    ///     algorithm the server supports. Requires <c>dnssecpolicysettings:read</c>.
    /// </summary>
    [HttpGet]
    [RequirePermission("dnssecpolicysettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(DnssecPolicySettingsDetailExample))]
    public ActionResult<DnssecPolicySettingsDetail> Get() =>
        Ok(new DnssecPolicySettingsDetail(algorithmPolicyOptions.CurrentValue.AllowedAlgorithms));

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:Dnssec</c> as an RFC 7396 merge
    ///     patch, through <see cref="PresetConfigurationFileApplier" />. The change takes effect at
    ///     once. Returns 400 for an unknown algorithm name. Requires
    ///     <c>dnssecpolicysettings:write</c>.
    /// </summary>
    [HttpPatch]
    [RequirePermission("dnssecpolicysettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(DnssecPolicySettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];

        if (
            patch.TryGetMergePatchStringList(
                "allowedAlgorithms",
                out List<string> allowedAlgorithms,
                allowNull: false
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    DnssecAlgorithmPolicyOptions.SectionName,
                    nameof(DnssecAlgorithmPolicyOptions.AllowedAlgorithms),
                    ParseAllowedAlgorithms(allowedAlgorithms)
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
    ///     This method parses an <c>allowedAlgorithms</c> field, a JSON array of DNSSEC algorithm
    ///     names in the same format as the <c>algorithm</c> field of <c>DnssecKeysController</c>
    ///     (not C# enum member names), into the list
    ///     <see cref="DnssecAlgorithmPolicyOptions.AllowedAlgorithms" />
    ///     binds from.
    /// </summary>
    private static List<string> ParseAllowedAlgorithms(List<string> names)
    {
        List<string> parsed = [];

        foreach (string item in names)
        {
            DnssecAlgorithmEnum algorithm;

            try
            {
                algorithm = JsonSerializer.Deserialize<DnssecAlgorithmEnum>(
                    JsonSerializer.Serialize(item)
                );
            }
            catch (JsonException)
            {
                throw new ValidationException($"'{item}' is not a valid DNSSEC algorithm name.");
            }

            // EnumDescriptionJsonConverter does not throw for an unknown name; it returns the
            // enum's default (zero) value, so the JsonException catch alone would accept a bad
            // name. Enum.IsDefined rejects it.
            if (!Enum.IsDefined(algorithm))
                throw new ValidationException($"'{item}' is not a valid DNSSEC algorithm name.");

            // A policy cannot allow what the server never signs with.
            if (DnssecAlgorithmRegistry.IsSigningProhibited(algorithm))
                throw new ValidationException(
                    $"DNSSEC algorithm '{item}' is prohibited for signing (RFC 8624 §3.1) and cannot be allowed."
                );

            parsed.Add(algorithm.ToString());
        }

        return parsed;
    }
}
