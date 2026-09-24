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
///     This controller reads and writes this host's own <see cref="DnsCookieOptions" /> (RFC
///     7873) -- the REST management surface for DNS Cookies, mirroring the surface
///     <see cref="RateLimitingSettingsController" /> already gives its sibling hardening
///     primitive. No secret material is involved. Every action requires the
///     <c>dnscookies:read</c> or <c>dnscookies:write</c> permission.
/// </summary>
/// <param name="cookieOptions">This host's own currently-effective DNS Cookie settings.</param>
/// <param name="runtimeConfigurationOptions">The JSON configuration file a write is merged into.</param>
/// <param name="clusterSyncRecorder">
///     The cluster config-sync helper this controller calls after a successful write so the change
///     propagates to every cluster peer.
/// </param>
/// <param name="stateRepository">
///     The repository <see cref="Get" /> reads this section's own last-write-wins bookkeeping
///     from, to surface <see cref="DnsCookieSettingsDetail.LastModifiedUtc" />/
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
    ///     This action reads this host's own currently-effective (live and running) DNS Cookie
    ///     settings.
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
    ///     This action merges a partial update into <c>DnsServer:Cookies</c> via
    ///     <see cref="PresetConfigurationFileApplier" />'s own generic settings-list overload, RFC
    ///     7396 merge-patch style: a field absent from <paramref name="patch" /> is left exactly as
    ///     it was in the file.
    /// </summary>
    [HttpPatch]
    [RequirePermission("dnscookies:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(DnsCookieSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];

        if (patch.TryGetMergePatchField("enable", out JsonElement enable))
            settings.Add(
                new PresetConfigurationSetting(
                    DnsCookieOptions.SectionName,
                    nameof(DnsCookieOptions.Enable),
                    enable.GetBoolean()
                )
            );

        if (patch.TryGetMergePatchField("require", out JsonElement require))
            settings.Add(
                new PresetConfigurationSetting(
                    DnsCookieOptions.SectionName,
                    nameof(DnsCookieOptions.Require),
                    require.GetBoolean()
                )
            );

        if (
            patch.TryGetMergePatchField(
                "secretRotationInterval",
                out JsonElement secretRotationInterval
            )
        )
        {
            if (!TimeSpan.TryParse(secretRotationInterval.GetString(), out TimeSpan parsed))
                throw new ValidationException(
                    "'secretRotationInterval' must be a valid TimeSpan (for example \"1.00:00:00\")."
                );

            settings.Add(
                new PresetConfigurationSetting(
                    DnsCookieOptions.SectionName,
                    nameof(DnsCookieOptions.SecretRotationInterval),
                    parsed.ToString()
                )
            );
        }

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
