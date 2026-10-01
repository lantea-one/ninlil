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
    ///     This action merges a partial update into the <c>Cluster</c> section (and, for
    ///     <c>addressReconcileInterval</c>, the <c>CatalogSync</c> section) as an RFC 7396 merge
    ///     patch, through <see cref="PresetConfigurationFileApplier" />. There is no <c>nodeId</c>
    ///     field. The response note says whether the change applies at once or needs a restart.
    ///     Returns 400 for a port outside 0-65535 (0 means the shared HTTPS port), a
    ///     missed-heartbeat threshold below 1, or a duration that is not a valid TimeSpan. Requires
    ///     <c>clustersettings:write</c>.
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

        if (patch.TryGetMergePatchField("enable", out JsonElement enable))
        {
            settings.Add(
                new PresetConfigurationSetting(
                    ClusterOptions.SectionName,
                    nameof(ClusterOptions.Enable),
                    enable.GetBoolean()
                )
            );
            touchedHotReloadingField = true;
        }

        if (patch.TryGetMergePatchField("advertisedHost", out JsonElement advertisedHostElement))
        {
            string advertisedHost = advertisedHostElement.GetString() ?? "";

            settings.Add(
                new PresetConfigurationSetting(
                    ClusterOptions.SectionName,
                    nameof(ClusterOptions.AdvertisedHost),
                    advertisedHost
                )
            );
            touchedHotReloadingField = true;
        }

        if (patch.TryGetMergePatchField("advertisedPort", out JsonElement advertisedPortElement))
        {
            int advertisedPort = advertisedPortElement.GetInt32();
            if (advertisedPort is < 0 or > 65535)
                throw new ValidationException(
                    "'advertisedPort' must be between 0 (use the shared HTTPS port) and 65535."
                );

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
            TryGetDuration(
                patch,
                "heartbeatInterval",
                out TimeSpan heartbeatInterval,
                out bool heartbeatIntervalPresent
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
        else if (heartbeatIntervalPresent)
            throw new ValidationException(
                "'heartbeatInterval' must be a valid TimeSpan (for example \"00:00:30\")."
            );

        if (
            TryGetDuration(
                patch,
                "heartbeatTimeout",
                out TimeSpan heartbeatTimeout,
                out bool heartbeatTimeoutPresent
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
        else if (heartbeatTimeoutPresent)
            throw new ValidationException(
                "'heartbeatTimeout' must be a valid TimeSpan (for example \"00:00:10\")."
            );

        if (
            patch.TryGetMergePatchField(
                "missedHeartbeatThreshold",
                out JsonElement missedHeartbeatThresholdElement
            )
        )
        {
            int missedHeartbeatThreshold = missedHeartbeatThresholdElement.GetInt32();
            if (missedHeartbeatThreshold < 1)
                throw new ValidationException("'missedHeartbeatThreshold' must be at least 1.");

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
            TryGetDuration(
                patch,
                "addressReconcileInterval",
                out TimeSpan addressReconcileInterval,
                out bool addressReconcileIntervalPresent
            )
        )
        {
            if (addressReconcileInterval <= TimeSpan.Zero)
                throw new ValidationException("'addressReconcileInterval' must be positive.");

            settings.Add(
                new PresetConfigurationSetting(
                    CatalogSyncOptions.SectionName,
                    nameof(CatalogSyncOptions.AddressReconcileInterval),
                    addressReconcileInterval.ToString()
                )
            );
            touchedRestartRequiringField = true;
        }
        else if (addressReconcileIntervalPresent)
            throw new ValidationException(
                "'addressReconcileInterval' must be a valid TimeSpan (for example \"00:01:00\")."
            );

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

    /// <summary>
    ///     This method reads a merge-patch field that should hold a TimeSpan string, telling the
    ///     caller both whether the field was present and whether it parsed, as the equivalent
    ///     helper in <see cref="RecursiveSettingsController" /> does.
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
