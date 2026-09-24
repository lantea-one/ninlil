using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Presets;
using Ninlil.Dns.Server.RateLimiting;
using Ninlil.Web.ClusterSync;
using Ninlil.Web.Examples.RateLimiting;
using Ninlil.Web.Examples.Settings;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller reads and writes this host's own <see cref="DnsRateLimitOptions" /> --
///     the REST management surface for rate limiting, mirroring the surface the block/allow-list
///     and forwarder features each already have of their own. No secret material is
///     involved, so every field is a plain read/write, unlike <see cref="EventBrokerSettingsController" />.
///     Every action requires the <c>ratelimiting:read</c> or <c>ratelimiting:write</c> permission.
/// </summary>
/// <param name="rateLimitOptions">This host's own currently-effective rate-limiting settings.</param>
/// <param name="runtimeConfigurationOptions">The JSON configuration file a write is merged into.</param>
/// <param name="clusterSyncRecorder">
///     The cluster config-sync helper this controller calls after a successful write so the change
///     propagates to every cluster peer.
/// </param>
/// <param name="stateRepository">
///     The repository <see cref="Get" /> reads this section's own last-write-wins bookkeeping
///     from, to surface <see cref="RateLimitingSettingsDetail.LastModifiedUtc" />/
///     <see cref="RateLimitingSettingsDetail.OriginatingNodeId" />.
/// </param>
[ApiController]
[Route("api/rate-limiting-settings")]
public class RateLimitingSettingsController(
    IOptions<DnsRateLimitOptions> rateLimitOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions,
    ClusterConfigSyncSettingsRecorder clusterSyncRecorder,
    IClusterSyncedSettingsStateRepository stateRepository
) : ControllerBase
{
    /// <summary>
    ///     This action reads this host's own currently-effective (live and running) rate-limiting
    ///     settings.
    /// </summary>
    [HttpGet]
    [RequirePermission("ratelimiting:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RateLimitingSettingsDetailExample))]
    public async Task<ActionResult<RateLimitingSettingsDetail>> Get(CancellationToken stoppingToken)
    {
        ClusterSyncedSettingsStateEntity state = await stateRepository.FindBySectionNameAsync(
            DnsRateLimitOptions.SectionName,
            stoppingToken
        );

        return Ok(
            new RateLimitingSettingsDetail(
                rateLimitOptions.Value.Enable,
                rateLimitOptions.Value.QueriesPerSecond,
                rateLimitOptions.Value.BurstSize,
                rateLimitOptions.Value.TrackedClientExpiry,
                rateLimitOptions.Value.CleanupInterval,
                rateLimitOptions.Value.MaxTrackedClients,
                rateLimitOptions.Value.SubnetPrefixLengthIPv4,
                rateLimitOptions.Value.SubnetPrefixLengthIPv6,
                rateLimitOptions.Value.SubnetSampleWindow,
                rateLimitOptions.Value.SubnetUdpQueryLimit,
                rateLimitOptions.Value.SubnetTcpQueryLimit,
                rateLimitOptions.Value.SubnetTruncatePercentage,
                rateLimitOptions.Value.SubnetBypassCidrs,
                state?.GetLastModifiedUtc(),
                state?.OriginatingNodeId
            )
        );
    }

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:RateLimiting</c> via
    ///     <see cref="PresetConfigurationFileApplier" />'s own generic settings-list overload, RFC
    ///     7396 merge-patch style: a field absent from <paramref name="patch" /> is left exactly as
    ///     it was in the file.
    /// </summary>
    [HttpPatch]
    [RequirePermission("ratelimiting:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(RateLimitingSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];

        if (patch.TryGetMergePatchField("enable", out JsonElement enable))
            settings.Add(
                new PresetConfigurationSetting(
                    DnsRateLimitOptions.SectionName,
                    nameof(DnsRateLimitOptions.Enable),
                    enable.GetBoolean()
                )
            );

        if (patch.TryGetMergePatchField("queriesPerSecond", out JsonElement queriesPerSecond))
            settings.Add(
                new PresetConfigurationSetting(
                    DnsRateLimitOptions.SectionName,
                    nameof(DnsRateLimitOptions.QueriesPerSecond),
                    queriesPerSecond.GetDouble()
                )
            );

        if (patch.TryGetMergePatchField("burstSize", out JsonElement burstSize))
            settings.Add(
                new PresetConfigurationSetting(
                    DnsRateLimitOptions.SectionName,
                    nameof(DnsRateLimitOptions.BurstSize),
                    burstSize.GetInt32()
                )
            );

        if (patch.TryGetMergePatchField("trackedClientExpiry", out JsonElement trackedClientExpiry))
        {
            if (!TimeSpan.TryParse(trackedClientExpiry.GetString(), out TimeSpan parsed))
                throw new ValidationException(
                    "'trackedClientExpiry' must be a valid TimeSpan (for example \"00:05:00\")."
                );

            settings.Add(
                new PresetConfigurationSetting(
                    DnsRateLimitOptions.SectionName,
                    nameof(DnsRateLimitOptions.TrackedClientExpiry),
                    parsed.ToString()
                )
            );
        }

        if (patch.TryGetMergePatchField("cleanupInterval", out JsonElement cleanupInterval))
        {
            if (!TimeSpan.TryParse(cleanupInterval.GetString(), out TimeSpan parsed))
                throw new ValidationException(
                    "'cleanupInterval' must be a valid TimeSpan (for example \"00:01:00\")."
                );

            settings.Add(
                new PresetConfigurationSetting(
                    DnsRateLimitOptions.SectionName,
                    nameof(DnsRateLimitOptions.CleanupInterval),
                    parsed.ToString()
                )
            );
        }

        if (patch.TryGetMergePatchField("maxTrackedClients", out JsonElement maxTrackedClients))
            settings.Add(
                new PresetConfigurationSetting(
                    DnsRateLimitOptions.SectionName,
                    nameof(DnsRateLimitOptions.MaxTrackedClients),
                    maxTrackedClients.GetInt32()
                )
            );

        if (
            patch.TryGetMergePatchField(
                "subnetPrefixLengthIPv4",
                out JsonElement subnetPrefixLengthIPv4
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    DnsRateLimitOptions.SectionName,
                    nameof(DnsRateLimitOptions.SubnetPrefixLengthIPv4),
                    (byte)subnetPrefixLengthIPv4.GetInt32()
                )
            );

        if (
            patch.TryGetMergePatchField(
                "subnetPrefixLengthIPv6",
                out JsonElement subnetPrefixLengthIPv6
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    DnsRateLimitOptions.SectionName,
                    nameof(DnsRateLimitOptions.SubnetPrefixLengthIPv6),
                    (byte)subnetPrefixLengthIPv6.GetInt32()
                )
            );

        if (patch.TryGetMergePatchField("subnetSampleWindow", out JsonElement subnetSampleWindow))
        {
            if (!TimeSpan.TryParse(subnetSampleWindow.GetString(), out TimeSpan parsedWindow))
                throw new ValidationException(
                    "'subnetSampleWindow' must be a valid TimeSpan (for example \"00:00:10\")."
                );

            settings.Add(
                new PresetConfigurationSetting(
                    DnsRateLimitOptions.SectionName,
                    nameof(DnsRateLimitOptions.SubnetSampleWindow),
                    parsedWindow.ToString()
                )
            );
        }

        if (patch.TryGetMergePatchField("subnetUdpQueryLimit", out JsonElement subnetUdpQueryLimit))
            settings.Add(
                new PresetConfigurationSetting(
                    DnsRateLimitOptions.SectionName,
                    nameof(DnsRateLimitOptions.SubnetUdpQueryLimit),
                    subnetUdpQueryLimit.GetInt32()
                )
            );

        if (patch.TryGetMergePatchField("subnetTcpQueryLimit", out JsonElement subnetTcpQueryLimit))
            settings.Add(
                new PresetConfigurationSetting(
                    DnsRateLimitOptions.SectionName,
                    nameof(DnsRateLimitOptions.SubnetTcpQueryLimit),
                    subnetTcpQueryLimit.GetInt32()
                )
            );

        if (
            patch.TryGetMergePatchField(
                "subnetTruncatePercentage",
                out JsonElement subnetTruncatePercentage
            )
        )
        {
            double truncatePercentage = subnetTruncatePercentage.GetDouble();

            if (truncatePercentage < 0 || truncatePercentage > 100)
                throw new ValidationException(
                    "'subnetTruncatePercentage' must be between 0 and 100."
                );

            settings.Add(
                new PresetConfigurationSetting(
                    DnsRateLimitOptions.SectionName,
                    nameof(DnsRateLimitOptions.SubnetTruncatePercentage),
                    truncatePercentage
                )
            );
        }

        if (patch.TryGetMergePatchField("subnetBypassCidrs", out JsonElement subnetBypassCidrs))
            settings.Add(
                new PresetConfigurationSetting(
                    DnsRateLimitOptions.SectionName,
                    nameof(DnsRateLimitOptions.SubnetBypassCidrs),
                    (List<string>)
                        [
                            .. subnetBypassCidrs
                                .EnumerateArray()
                                .Select(element => element.GetString()),
                        ]
                )
            );

        if (settings.Count == 0)
            return Ok(new RuntimeSettingsWriteResult("Nothing to change -- the patch was empty."));

        await PresetConfigurationFileApplier.ApplyAsync(
            settings,
            runtimeConfigurationOptions.ConfigurationFilePath
        );

        await clusterSyncRecorder.RecordAndPublishAsync(
            DnsRateLimitOptions.SectionName,
            settings,
            HttpContext.RequestAborted
        );

        return Ok(new RuntimeSettingsWriteResult(RuntimeSettingsWriteResult.RestartRequiredNote));
    }
}
