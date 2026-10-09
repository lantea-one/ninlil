using System.Net;
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
///     This controller reads and writes <see cref="DnsRateLimitOptions" />: the per-client DNS
///     query rate limit (rate, burst, client tracking) and the per-subnet limits (prefix lengths,
///     sample window, UDP and TCP limits, truncation share, bypass networks). Reading needs
///     <c>ratelimiting:read</c> and writing needs <c>ratelimiting:write</c>. A write is also
///     replicated to the other cluster nodes and needs a restart, since the settings are read
///     through <see cref="IOptions{TOptions}" />.
/// </summary>
/// <param name="rateLimitOptions">The rate-limiting settings loaded at startup.</param>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file a write is merged into.</param>
/// <param name="clusterSyncRecorder">
///     Records a successful write and sends it to every cluster peer.
/// </param>
/// <param name="stateRepository">
///     Supplies the last-write-wins bookkeeping that <see cref="Get" /> reports as
///     <see cref="RateLimitingSettingsDetail.LastModifiedUtc" /> and
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
    ///     This action returns the rate-limiting settings in use (as loaded at startup) and which
    ///     node last changed them. Requires <c>ratelimiting:read</c>.
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
    ///     This action merges a partial update into <c>DnsServer:RateLimiting</c> as an RFC 7396
    ///     merge patch, through <see cref="PresetConfigurationFileApplier" />. A field absent from
    ///     <paramref name="patch" /> keeps its value; <c>subnetBypassCidrs</c> replaces the whole
    ///     list. The change needs a restart. Returns 400 for a field of the wrong JSON type, a rate,
    ///     bucket size, tracked-client ceiling or subnet query limit below 1, a duration that is
    ///     not positive or exceeds the timer limit, a subnet prefix length outside 1-32 (IPv4) or
    ///     1-128 (IPv6), a truncation percentage outside 0-100, or a bypass entry that is not a
    ///     CIDR network address (for example <c>10.0.0.1/8</c>). Requires <c>ratelimiting:write</c>.
    /// </summary>
    [HttpPatch]
    [RequirePermission("ratelimiting:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(RateLimitingSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];

        if (patch.TryGetMergePatchBoolean("enable", out bool enable))
            settings.Add(
                new PresetConfigurationSetting(
                    DnsRateLimitOptions.SectionName,
                    nameof(DnsRateLimitOptions.Enable),
                    enable
                )
            );

        // A rate or a bucket of zero would block every client, so each is at least 1 (the rate
        // may be fractional).
        if (
            patch.TryGetMergePatchDouble(
                "queriesPerSecond",
                out double queriesPerSecond,
                0.001,
                1_000_000_000
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    DnsRateLimitOptions.SectionName,
                    nameof(DnsRateLimitOptions.QueriesPerSecond),
                    queriesPerSecond
                )
            );

        if (patch.TryGetMergePatchInt32("burstSize", out int burstSize, 1, int.MaxValue))
            settings.Add(
                new PresetConfigurationSetting(
                    DnsRateLimitOptions.SectionName,
                    nameof(DnsRateLimitOptions.BurstSize),
                    burstSize
                )
            );

        // The three durations below reach Task.Delay (the cleanup service) or a window comparison,
        // so each must be positive and no longer than a timer accepts.
        if (
            patch.TryGetMergePatchDuration(
                "trackedClientExpiry",
                out TimeSpan trackedClientExpiry,
                TimeSpan.FromTicks(1),
                JsonElementMergePatchExtension.TimerMaximum
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    DnsRateLimitOptions.SectionName,
                    nameof(DnsRateLimitOptions.TrackedClientExpiry),
                    trackedClientExpiry.ToString()
                )
            );

        if (
            patch.TryGetMergePatchDuration(
                "cleanupInterval",
                out TimeSpan cleanupInterval,
                TimeSpan.FromTicks(1),
                JsonElementMergePatchExtension.TimerMaximum
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    DnsRateLimitOptions.SectionName,
                    nameof(DnsRateLimitOptions.CleanupInterval),
                    cleanupInterval.ToString()
                )
            );

        if (
            patch.TryGetMergePatchInt32(
                "maxTrackedClients",
                out int maxTrackedClients,
                1,
                int.MaxValue
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    DnsRateLimitOptions.SectionName,
                    nameof(DnsRateLimitOptions.MaxTrackedClients),
                    maxTrackedClients
                )
            );

        // A prefix length of 0 would put every client into one subnet bucket.
        if (
            patch.TryGetMergePatchInt32(
                "subnetPrefixLengthIPv4",
                out int subnetPrefixLengthIPv4,
                1,
                32
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    DnsRateLimitOptions.SectionName,
                    nameof(DnsRateLimitOptions.SubnetPrefixLengthIPv4),
                    subnetPrefixLengthIPv4
                )
            );

        if (
            patch.TryGetMergePatchInt32(
                "subnetPrefixLengthIPv6",
                out int subnetPrefixLengthIPv6,
                1,
                128
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    DnsRateLimitOptions.SectionName,
                    nameof(DnsRateLimitOptions.SubnetPrefixLengthIPv6),
                    subnetPrefixLengthIPv6
                )
            );

        if (
            patch.TryGetMergePatchDuration(
                "subnetSampleWindow",
                out TimeSpan subnetSampleWindow,
                TimeSpan.FromTicks(1),
                JsonElementMergePatchExtension.TimerMaximum
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    DnsRateLimitOptions.SectionName,
                    nameof(DnsRateLimitOptions.SubnetSampleWindow),
                    subnetSampleWindow.ToString()
                )
            );

        if (
            patch.TryGetMergePatchInt32(
                "subnetUdpQueryLimit",
                out int subnetUdpQueryLimit,
                1,
                int.MaxValue
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    DnsRateLimitOptions.SectionName,
                    nameof(DnsRateLimitOptions.SubnetUdpQueryLimit),
                    subnetUdpQueryLimit
                )
            );

        if (
            patch.TryGetMergePatchInt32(
                "subnetTcpQueryLimit",
                out int subnetTcpQueryLimit,
                1,
                int.MaxValue
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    DnsRateLimitOptions.SectionName,
                    nameof(DnsRateLimitOptions.SubnetTcpQueryLimit),
                    subnetTcpQueryLimit
                )
            );

        if (
            patch.TryGetMergePatchDouble(
                "subnetTruncatePercentage",
                out double truncatePercentage,
                0,
                100
            )
        )
            settings.Add(
                new PresetConfigurationSetting(
                    DnsRateLimitOptions.SectionName,
                    nameof(DnsRateLimitOptions.SubnetTruncatePercentage),
                    truncatePercentage
                )
            );

        // The limiter silently skips an entry it cannot match, so an entry that is not a network
        // address (for example 10.0.0.1/8, with host bits set) is rejected here instead.
        if (
            patch.TryGetMergePatchStringList(
                "subnetBypassCidrs",
                out List<string> subnetBypassCidrs,
                false
            )
        )
        {
            foreach (string cidr in subnetBypassCidrs)
            {
                if (!IsNetworkAddress(cidr))
                    throw new ValidationException(
                        $"'{cidr}' is not a valid CIDR network (for example \"10.0.0.0/8\")."
                    );
            }

            settings.Add(
                new PresetConfigurationSetting(
                    DnsRateLimitOptions.SectionName,
                    nameof(DnsRateLimitOptions.SubnetBypassCidrs),
                    subnetBypassCidrs
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
            DnsRateLimitOptions.SectionName,
            settings,
            HttpContext.RequestAborted
        );

        return Ok(new RuntimeSettingsWriteResult(RuntimeSettingsWriteResult.RestartRequiredNote));
    }

    /// <summary>
    ///     This method checks that <paramref name="cidr" /> names a network address: it parses as
    ///     a CIDR network and has no host bits set after the prefix. The limiter's
    ///     <c>TokenBucketDnsRateLimiter</c> compares the leading prefix bits of the entry's
    ///     address with the client's, so <c>10.0.0.1/8</c> would never match what an operator
    ///     expects. <see cref="IPNetwork.TryParse(string, out IPNetwork)" /> alone accepts such an
    ///     entry, so the host bits are checked here.
    /// </summary>
    private static bool IsNetworkAddress(string cidr)
    {
        if (!IPNetwork.TryParse(cidr, out IPNetwork network))
            return false;

        if (!IPAddress.TryParse(cidr.AsSpan(0, cidr.IndexOf('/')), out IPAddress address))
            return false;

        byte[] addressBytes = address.GetAddressBytes();
        byte[] baseBytes = network.BaseAddress.GetAddressBytes();

        return addressBytes.AsSpan().SequenceEqual(baseBytes);
    }
}
