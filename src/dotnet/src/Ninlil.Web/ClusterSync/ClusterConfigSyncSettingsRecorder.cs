using System.Text.Json;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.Core.ClusterSync;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Presets;

// Define our namespace.
namespace Ninlil.Web.ClusterSync;

/// <summary>
///     This class is the sending side of cluster sync for settings sections. The four settings
///     controllers that sync across a cluster (<c>EventBrokerSettingsController</c>,
///     <c>RateLimitingSettingsController</c>, <c>DnsCookieSettingsController</c> and
///     <c>ReverseProxySettingsController</c>) call it after merging a write into the runtime
///     configuration file with <c>PresetConfigurationFileApplier</c>. It records the section's
///     last-write-wins state (<see cref="ClusterSyncedSettingsStateEntity" />) and publishes a
///     <see cref="ClusterConfigSettingsChangedEvent" />, which
///     <c>SettingsClusterSyncEventSubscriber</c> forwards to every peer, where
///     <see cref="SettingsClusterConfigSyncApplier" /> applies it. It is registered as a scoped
///     service in <c>Program.cs</c>.
/// </summary>
/// <param name="stateRepository">Reads and writes the sync state of a section.</param>
/// <param name="unitOfWork">Commits the state write.</param>
/// <param name="clusterOptions">The cluster settings, read for this node's id.</param>
/// <param name="eventPublisher">Publishes the <see cref="ClusterConfigSettingsChangedEvent" />.</param>
public class ClusterConfigSyncSettingsRecorder(
    IClusterSyncedSettingsStateRepository stateRepository,
    IUnitOfWork unitOfWork,
    IOptionsMonitor<ClusterOptions> clusterOptions,
    IDnsServerEventPublisher eventPublisher
)
{
    /// <summary>
    ///     This method records a settings write that has just been applied, with this node as its
    ///     origin, and publishes it to the cluster. Each value is sent as its
    ///     <see cref="object.ToString" /> text (see <see cref="SettingsSyncEntry" />).
    /// </summary>
    /// <param name="sectionName">
    ///     The section name, the options class's <c>SectionName</c> (for example
    ///     <c>"DnsServer:Cookies"</c>). Peers receive it as
    ///     <see cref="ClusterConfigSyncEnvelope.EntityKey" /> and look up their state row by it, so
    ///     every node must use the same value.
    /// </param>
    /// <param name="settings">The configuration values that were just written.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    public async Task RecordAndPublishAsync(
        string sectionName,
        IReadOnlyList<PresetConfigurationSetting> settings,
        CancellationToken stoppingToken
    )
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid ownNodeId = clusterOptions.CurrentValue.NodeId;

        ClusterSyncedSettingsStateEntity state = await stateRepository.FindBySectionNameAsync(
            sectionName,
            stoppingToken
        );

        if (state is null)
        {
            state = new ClusterSyncedSettingsStateEntity
            {
                Id = Guid.NewGuid(),
                SectionName = sectionName,
                CreatedAtUtc = now,
                OriginatingNodeId = ownNodeId,
            };
            await stateRepository.AddAsync(state, stoppingToken);
        }
        else
        {
            state.UpdatedAtUtc = now;
            state.OriginatingNodeId = ownNodeId;
            stateRepository.Update(state);
        }

        await unitOfWork.SaveChangesAsync(stoppingToken);

        string settingsJson = JsonSerializer.Serialize(
            settings
                .Select(setting => new SettingsSyncEntry(
                    setting.SectionPath,
                    setting.Key,
                    setting.Value?.ToString()
                ))
                .ToList()
        );

        eventPublisher.Publish(
            new ClusterConfigSettingsChangedEvent(now, sectionName, settingsJson)
        );
    }
}
