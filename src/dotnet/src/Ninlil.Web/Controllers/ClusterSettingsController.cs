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
///     This controller reads and writes this node's own <see cref="ClusterOptions" /> -- whether
///     clustering is on at all, this node's own advertised address, and the heartbeat engine's
///     own tuning. This is the settings surface a peer-management workflow
///     (<see cref="ClusterController" />) is otherwise unreachable without: an operator must set
///     <see cref="ClusterOptions.Enable" />/<see cref="ClusterOptions.AdvertisedHost" /> here
///     before either <see cref="ClusterController.CreateInvite" /> or
///     <see cref="ClusterController.Join" /> can succeed, since both throw a
///     <see cref="ValidationException" /> while either is unset. Every action requires the
///     <c>clustersettings:read</c> or <c>clustersettings:write</c> permission.
///     <para>
///         Most fields take effect immediately, with no restart -- <see cref="ClusterOptions" />
///         is read via <see cref="IOptionsMonitor{TOptions}" /> throughout <c>Server.Cluster</c>,
///         the same live-reload shape <see cref="RecursiveSettingsController" /> already
///         establishes. <see cref="ClusterOptions.HeartbeatInterval" /> is the one exception:
///         <c>ClusterHeartbeatBackgroundService</c> reads it once, into its own
///         <see cref="PeriodicTimer" />, at startup -- see that class's own doc comment.
///         <see cref="ClusterOptions.NodeId" /> is never writable here at all; see that
///         property's own doc comment for why. This controller also folds in the one field of
///         <see cref="CatalogSyncOptions" /> (<see cref="CatalogSyncOptions.AddressReconcileInterval" />)
///         -- a genuinely distinct options class, but cluster-adjacent enough (it exists purely to
///         keep a catalog-managed zone's own address lists reconciled against this node's own live
///         cluster membership) that a whole separate controller/page for one
///         <see cref="TimeSpan" /> field was judged overkill; see
///         <see cref="ClusterSettingsDetail" />'s own doc comment. Like
///         <see cref="ClusterOptions.HeartbeatInterval" />, this field also needs a restart:
///         <c>CatalogClusterAddressSyncService</c> reads it once, into its own
///         <see cref="PeriodicTimer" />, at startup too.
///     </para>
/// </summary>
/// <param name="clusterOptions">This node's own currently-effective (live) cluster settings.</param>
/// <param name="catalogSyncOptions">This node's own currently-effective (live) catalog address-reconciliation settings.</param>
/// <param name="runtimeConfigurationOptions">The JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/cluster-settings")]
public class ClusterSettingsController(
    IOptionsMonitor<ClusterOptions> clusterOptions,
    IOptionsMonitor<CatalogSyncOptions> catalogSyncOptions,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action reads this node's own currently-effective (live) cluster settings.
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
    ///     This action merges a partial update into <c>Cluster</c> (and, for
    ///     <c>addressReconcileInterval</c>, the separate <c>CatalogSync</c> section) via
    ///     <see cref="PresetConfigurationFileApplier" />'s own generic settings-list overload, RFC
    ///     7396 merge-patch style. <see cref="ClusterOptions.NodeId" /> has no field here at all --
    ///     it is generated once, automatically, and never operator-editable.
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
    ///     This method reads a merge-patch field expected to hold a <see cref="TimeSpan" />-parseable
    ///     string, distinguishing "absent" from "present but unparseable" for the caller -- the
    ///     same helper shape <see cref="RecursiveSettingsController" />'s own equivalent already
    ///     establishes.
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
