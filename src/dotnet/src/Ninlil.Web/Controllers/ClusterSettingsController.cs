using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Authoritative.CatalogSync;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Examples.Cluster;
using Ninlil.Web.Examples.Settings;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller reads and writes this node's <see cref="ClusterOptions" />: whether
///     clustering is on, the address this node advertises to peers, and heartbeat tuning. An
///     operator must set <see cref="ClusterOptions.Enable" /> and
///     <see cref="ClusterOptions.AdvertisedHost" /> here before
///     <see cref="ClusterController.CreateInvite" /> or <see cref="ClusterController.Join" /> can
///     succeed; both return 400 while either is unset. Reading needs
///     <c>clustersettings:read</c> and writing needs <c>clustersettings:write</c>.
///     <para>
///         Most fields take effect at once, because <c>Server.Cluster</c> reads
///         <see cref="ClusterOptions" /> through <see cref="IOptionsMonitor{TOptions}" />.
///         <see cref="ClusterOptions.HeartbeatInterval" /> needs a restart, because
///         <c>ClusterHeartbeatBackgroundService</c> builds its <see cref="PeriodicTimer" /> from
///         it at startup. <see cref="ClusterOptions.NodeId" /> is generated once and cannot be
///         written here.
///     </para>
///     <para>
///         This controller also carries the one field of <see cref="CatalogSyncOptions" />,
///         <see cref="CatalogSyncOptions.AddressReconcileInterval" />, which controls how often
///         catalog-managed zones' address lists are reconciled with the cluster membership (see
///         <see cref="ClusterSettingsDetail" />). It too needs a restart, since
///         <c>CatalogClusterAddressSyncService</c> builds its <see cref="PeriodicTimer" /> at
///         startup.
///     </para>
/// </summary>
/// <param name="clusterOptions">This node's current cluster settings.</param>
/// <param name="catalogSyncOptions">This node's current catalog address-reconciliation settings.</param>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/cluster-settings")]
public class ClusterSettingsController(
    IOptionsMonitor<ClusterOptions> clusterOptions,
    IOptionsMonitor<CatalogSyncOptions> catalogSyncOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action returns this node's current cluster settings, including its node id.
    ///     Requires <c>clustersettings:read</c>.
    /// </summary>
    [HttpGet]
    [RequirePermission("clustersettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ClusterSettingsDetailExample))]
    public ActionResult<ClusterSettingsDetail> Get()
    {
        ClusterOptions current = clusterOptions.CurrentValue;

        return Ok(
            new ClusterSettingsDetail(
                current.Enable,
                current.NodeId,
                current.AdvertisedHost,
                current.AdvertisedPort,
                current.HeartbeatInterval,
                current.HeartbeatTimeout,
                current.MissedHeartbeatThreshold,
                catalogSyncOptions.CurrentValue.AddressReconcileInterval
            )
        );
    }

    /// <summary>
    ///     The shortest accepted <c>heartbeatInterval</c>. The value feeds a
    ///     <see cref="PeriodicTimer" /> at startup, before the <c>Enable</c> check, and that
    ///     constructor throws for zero or a negative period.
    /// </summary>
    private static readonly TimeSpan HeartbeatIntervalMinimum = TimeSpan.FromSeconds(1);

    /// <summary>The longest accepted <c>heartbeatInterval</c> (one day).</summary>
    private static readonly TimeSpan HeartbeatIntervalMaximum = TimeSpan.FromDays(1);

    /// <summary>
    ///     The shortest accepted <c>heartbeatTimeout</c>. The value becomes the
    ///     <c>HttpClient.Timeout</c> of every peer call, which throws for zero or a negative
    ///     timeout and would mark every peer unreachable.
    /// </summary>
    private static readonly TimeSpan HeartbeatTimeoutMinimum = TimeSpan.FromSeconds(1);

    /// <summary>The longest accepted <c>heartbeatTimeout</c> (five minutes).</summary>
    private static readonly TimeSpan HeartbeatTimeoutMaximum = TimeSpan.FromMinutes(5);

    /// <summary>
    ///     This action merges a partial update into the <c>Cluster</c> section (and, for
    ///     <c>addressReconcileInterval</c>, the <c>CatalogSync</c> section) as an RFC 7396 merge
    ///     patch, through <see cref="PresetConfigurationFileApplier" />. There is no <c>nodeId</c>
    ///     field. The response note says whether the change applies at once or needs a restart.
    ///     Returns 400 for a field of the wrong JSON type, a port outside 0-65535 (0 means the
    ///     shared HTTPS port), a missed-heartbeat threshold below 1, a <c>heartbeatInterval</c>
    ///     outside 1 second to 1 day, a <c>heartbeatTimeout</c> outside 1 second to 5 minutes, or
    ///     an <c>addressReconcileInterval</c> that is not positive or exceeds the timer limit.
    ///     Requires <c>clustersettings:write</c>.
    /// </summary>
    [HttpPatch]
    [RequirePermission("clustersettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(ClusterSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];
        bool touchedRestartRequiringField = false;
        bool touchedHotReloadingField = false;

        if (patch.TryGetMergePatchBoolean("enable", out bool enable))
        {
            settings.Add(
                new PresetConfigurationSetting(
                    ClusterOptions.SectionName,
                    nameof(ClusterOptions.Enable),
                    enable
                )
            );
            touchedHotReloadingField = true;
        }

        if (patch.TryGetMergePatchString("advertisedHost", out string advertisedHost, true))
        {
            settings.Add(
                new PresetConfigurationSetting(
                    ClusterOptions.SectionName,
                    nameof(ClusterOptions.AdvertisedHost),
                    advertisedHost ?? ""
                )
            );
            touchedHotReloadingField = true;
        }

        if (patch.TryGetMergePatchInt32("advertisedPort", out int advertisedPort, 0, 65535))
        {
            settings.Add(
                new PresetConfigurationSetting(
                    ClusterOptions.SectionName,
                    nameof(ClusterOptions.AdvertisedPort),
                    advertisedPort
                )
            );
            touchedHotReloadingField = true;
        }

        if (
            patch.TryGetMergePatchDuration(
                "heartbeatInterval",
                out TimeSpan heartbeatInterval,
                HeartbeatIntervalMinimum,
                HeartbeatIntervalMaximum
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    ClusterOptions.SectionName,
                    nameof(ClusterOptions.HeartbeatInterval),
                    heartbeatInterval.ToString()
                )
            );
            touchedRestartRequiringField = true;
        }

        if (
            patch.TryGetMergePatchDuration(
                "heartbeatTimeout",
                out TimeSpan heartbeatTimeout,
                HeartbeatTimeoutMinimum,
                HeartbeatTimeoutMaximum
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    ClusterOptions.SectionName,
                    nameof(ClusterOptions.HeartbeatTimeout),
                    heartbeatTimeout.ToString()
                )
            );
            touchedHotReloadingField = true;
        }

        if (
            patch.TryGetMergePatchInt32(
                "missedHeartbeatThreshold",
                out int missedHeartbeatThreshold,
                1,
                int.MaxValue
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    ClusterOptions.SectionName,
                    nameof(ClusterOptions.MissedHeartbeatThreshold),
                    missedHeartbeatThreshold
                )
            );
            touchedHotReloadingField = true;
        }

        if (
            patch.TryGetMergePatchDuration(
                "addressReconcileInterval",
                out TimeSpan addressReconcileInterval,
                TimeSpan.FromTicks(1),
                JsonElementMergePatchExtension.TimerMaximum
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    CatalogSyncOptions.SectionName,
                    nameof(CatalogSyncOptions.AddressReconcileInterval),
                    addressReconcileInterval.ToString()
                )
            );
            touchedRestartRequiringField = true;
        }

        if (settings.Count == 0)
            return Ok(new RuntimeSettingsWriteResult("Nothing to change -- the patch was empty."));

        await PresetConfigurationFileApplier.ApplyAsync(
            settings,
            runtimeConfigurationOptions.ConfigurationFilePath
        );

        string note = (touchedHotReloadingField, touchedRestartRequiringField) switch
        {
            (true, true) => RuntimeSettingsWriteResult.MixedReloadNote,
            (false, true) => RuntimeSettingsWriteResult.RestartRequiredNote,
            _ => RuntimeSettingsWriteResult.HotReloadedNote,
        };

        return Ok(new RuntimeSettingsWriteResult(note));
    }
}
