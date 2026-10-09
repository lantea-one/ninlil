using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Core.Cookies;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.ClusterSync;
using Ninlil.Web.Examples.DnsCookies;
using Ninlil.Web.Examples.Settings;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller reads and writes <see cref="DnsCookieOptions" /> (RFC 7873 DNS Cookies):
///     whether cookies are on, whether they are required, and how often the server secret
///     rotates. Reading needs <c>dnscookies:read</c> and writing needs <c>dnscookies:write</c>. A
///     write is also replicated to the other cluster nodes and needs a restart, since the
///     settings are read through <see cref="IOptions{TOptions}" />.
/// </summary>
/// <param name="cookieOptions">The DNS Cookie settings loaded at startup.</param>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file a write is merged into.</param>
/// <param name="clusterSyncRecorder">
///     Records a successful write and sends it to every cluster peer.
/// </param>
/// <param name="stateRepository">
///     Supplies the last-write-wins bookkeeping that <see cref="Get" /> reports as
///     <see cref="DnsCookieSettingsDetail.LastModifiedUtc" /> and
///     <see cref="DnsCookieSettingsDetail.OriginatingNodeId" />.
/// </param>
[ApiController]
[Route("api/dns-cookie-settings")]
public class DnsCookieSettingsController(
    IOptions<DnsCookieOptions> cookieOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions,
    ClusterConfigSyncSettingsRecorder clusterSyncRecorder,
    IClusterSyncedSettingsStateRepository stateRepository
) : ControllerBase
{
    /// <summary>
    ///     This action returns the DNS Cookie settings in use (as loaded at startup) and which node
    ///     last changed them. Requires <c>dnscookies:read</c>.
    /// </summary>
    [HttpGet]
    [RequirePermission("dnscookies:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(DnsCookieSettingsDetailExample))]
    public async Task<ActionResult<DnsCookieSettingsDetail>> Get(CancellationToken stoppingToken)
    {
        ClusterSyncedSettingsStateEntity state = await stateRepository.FindBySectionNameAsync(
            DnsCookieOptions.SectionName,
            stoppingToken
        );

        return Ok(
            new DnsCookieSettingsDetail(
                cookieOptions.Value.Enable,
                cookieOptions.Value.Require,
                cookieOptions.Value.SecretRotationInterval,
                state?.GetLastModifiedUtc(),
                state?.OriginatingNodeId
            )
        );
    }

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:Cookies</c> as an RFC 7396 merge
    ///     patch, through <see cref="PresetConfigurationFileApplier" />. A field absent from
    ///     <paramref name="patch" /> keeps its value. The change needs a restart. Returns 400 for a
    ///     field of the wrong JSON type or a rotation interval outside 1 minute to the timer
    ///     limit (about 49 days). Requires <c>dnscookies:write</c>.
    /// </summary>
    [HttpPatch]
    [RequirePermission("dnscookies:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(DnsCookieSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];

        if (patch.TryGetMergePatchBoolean("enable", out bool enable))
            settings.Add(
                new PresetConfigurationSetting(
                    DnsCookieOptions.SectionName,
                    nameof(DnsCookieOptions.Enable),
                    enable
                )
            );

        if (patch.TryGetMergePatchBoolean("require", out bool require))
            settings.Add(
                new PresetConfigurationSetting(
                    DnsCookieOptions.SectionName,
                    nameof(DnsCookieOptions.Require),
                    require
                )
            );

        // DnsCookieService builds a Timer from this period; a Timer throws above the timer limit.
        if (
            patch.TryGetMergePatchDuration(
                "secretRotationInterval",
                out TimeSpan secretRotationInterval,
                TimeSpan.FromMinutes(1),
                JsonElementMergePatchExtension.TimerMaximum
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    DnsCookieOptions.SectionName,
                    nameof(DnsCookieOptions.SecretRotationInterval),
                    secretRotationInterval.ToString()
                )
            );

        if (settings.Count == 0)
            return Ok(new RuntimeSettingsWriteResult("Nothing to change -- the patch was empty."));

        await PresetConfigurationFileApplier.ApplyAsync(
            settings,
            runtimeConfigurationOptions.ConfigurationFilePath
        );

        await clusterSyncRecorder.RecordAndPublishAsync(
            DnsCookieOptions.SectionName,
            settings,
            HttpContext.RequestAborted
        );

        return Ok(new RuntimeSettingsWriteResult(RuntimeSettingsWriteResult.RestartRequiredNote));
    }
}
