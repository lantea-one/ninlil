using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Presets;
using Ninlil.Dns.Server.Rewrites;
using Ninlil.Web.Examples.RewriteSettings;
using Ninlil.Web.Examples.Settings;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller reads and writes <see cref="DnsRewriteOptions" />: whether the rewrite
///     engine is on and the TTL of the answers it builds. The rewrites themselves are managed by
///     <c>DnsRewritesController</c>. Reading needs <c>rewritesettings:read</c> and writing needs
///     <c>rewritesettings:write</c>.
///     <para>
///         <b>Every field takes effect at once, with no restart.</b> <c>DnsRewriteEngine</c> reads
///         <see cref="DnsRewriteOptions" /> through <see cref="IOptionsMonitor{TOptions}" /> and is
///         registered whether or not rewrites were on at startup, so a fresh install (rewrites
///         off by default) can turn them on live. See <c>ServiceCollectionRewritesExtension</c>.
///     </para>
/// </summary>
/// <param name="rewriteOptions">The current rewrite settings.</param>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/rewrite-settings")]
public class RewriteSettingsController(
    IOptionsMonitor<DnsRewriteOptions> rewriteOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action returns the current rewrite settings. Requires <c>rewritesettings:read</c>.
    /// </summary>
    [HttpGet]
    [RequirePermission("rewritesettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RewriteSettingsDetailExample))]
    public ActionResult<RewriteSettingsDetail> Get() => Ok(ToDetail(rewriteOptions.CurrentValue));

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:Rewrites</c> as an RFC 7396 merge
    ///     patch, through <see cref="PresetConfigurationFileApplier" />. A field absent from
    ///     <paramref name="patch" /> keeps its value. Every field takes effect at once. Requires
    ///     <c>rewritesettings:write</c>.
    /// </summary>
    [HttpPatch]
    [RequirePermission("rewritesettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(RewriteSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];

        if (patch.TryGetMergePatchBoolean("enable", out bool enable))
            settings.Add(
                new PresetConfigurationSetting(
                    DnsRewriteOptions.SectionName,
                    nameof(DnsRewriteOptions.Enable),
                    enable
                )
            );

        // RFC 2181 §8: a TTL is at most 2^31 - 1.
        if (
            patch.TryGetMergePatchInt32(
                "timeToLiveSeconds",
                out int timeToLiveSeconds,
                0,
                int.MaxValue
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    DnsRewriteOptions.SectionName,
                    nameof(DnsRewriteOptions.TimeToLiveSeconds),
                    (uint)timeToLiveSeconds
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
    ///     This method builds <see cref="RewriteSettingsDetail" /> from the current
    ///     <see cref="DnsRewriteOptions" />.
    /// </summary>
    private static RewriteSettingsDetail ToDetail(DnsRewriteOptions options) =>
        new(options.Enable, options.TimeToLiveSeconds);
}
