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
///     This controller reads and writes this host's own <see cref="DnsRewriteOptions" /> -- the
///     engine-level toggle and synthesized-answer TTL for DNS rewrites. Rewrite entries themselves
///     (each individual domain/target override) are already covered by
///     <c>DnsRewritesController</c>; this controller covers only whether the rewrite engine
///     consults them at query time at all. No secret material is involved, so every field is a
///     plain read/write, the same shape <see cref="RateLimitingSettingsController" /> already
///     establishes. Every action requires the <c>rewritesettings:read</c> or
///     <c>rewritesettings:write</c> permission.
///     <para>
///         <b>Every field takes effect immediately, with no restart.</b> <see cref="DnsRewriteOptions" />
///         is read via <see cref="IOptionsMonitor{TOptions}" /> everywhere it actually matters for a
///         live query (<c>DnsRewriteEngine</c>), and <c>DnsRewriteEngine</c> is now always
///         registered as a DI singleton regardless of <see cref="DnsRewriteOptions.Enable" />'s own
///         value at process startup -- a structural fix alongside the hot-reload conversion, since
///         registering it only when already enabled at startup (this project's original shape)
///         would have meant a fresh install (which starts with rewrites off by design) could never
///         turn them on live at all, the same structural gap already found and fixed for
///         DNS-over-HTTPS's own route mapping. See <c>ServiceCollectionRewritesExtension</c>'s own
///         doc comment for the full reasoning.
///     </para>
/// </summary>
/// <param name="rewriteOptions">This host's own currently-effective rewrite settings, live.</param>
/// <param name="runtimeConfigurationOptions">The JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/rewrite-settings")]
public class RewriteSettingsController(
    IOptionsMonitor<DnsRewriteOptions> rewriteOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action reads this host's own currently-effective (live, even without a restart)
    ///     rewrite settings.
    /// </summary>
    [HttpGet]
    [RequirePermission("rewritesettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RewriteSettingsDetailExample))]
    public ActionResult<RewriteSettingsDetail> Get() => Ok(ToDetail(rewriteOptions.CurrentValue));

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:Rewrites</c> via
    ///     <see cref="PresetConfigurationFileApplier" />'s own generic settings-list overload, RFC
    ///     7396 merge-patch style: a field absent from <paramref name="patch" /> is left exactly as
    ///     it was in the file. Every field here hot-reloads immediately -- see this class's own doc
    ///     comment.
    /// </summary>
    [HttpPatch]
    [RequirePermission("rewritesettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(RewriteSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];

        if (patch.TryGetMergePatchField("enable", out JsonElement enable))
            settings.Add(
                new PresetConfigurationSetting(
                    DnsRewriteOptions.SectionName,
                    nameof(DnsRewriteOptions.Enable),
                    enable.GetBoolean()
                )
            );

        if (patch.TryGetMergePatchField("timeToLiveSeconds", out JsonElement timeToLiveSeconds))
            settings.Add(
                new PresetConfigurationSetting(
                    DnsRewriteOptions.SectionName,
                    nameof(DnsRewriteOptions.TimeToLiveSeconds),
                    timeToLiveSeconds.GetUInt32()
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
    ///     This method builds <see cref="RewriteSettingsDetail" /> from a live
    ///     <see cref="DnsRewriteOptions" /> snapshot.
    /// </summary>
    private static RewriteSettingsDetail ToDetail(DnsRewriteOptions options) =>
        new(options.Enable, options.TimeToLiveSeconds);
}
