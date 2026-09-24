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
///     This class is the one shared helper every settings controller operational config sync
///     covers (<c>EventBrokerSettingsController</c>/<c>RateLimitingSettingsController</c>/
///     <c>DnsCookieSettingsController</c>/<c>ReverseProxySettingsController</c>) calls immediately
///     after it has already merged a write into the runtime configuration file via
///     <c>PresetConfigurationFileApplier</c>: it records this node's own last-write-wins
///     bookkeeping for that section (<see cref="ClusterSyncedSettingsStateEntity" />) and
///     publishes <see cref="ClusterConfigSettingsChangedEvent" /> so
///     <c>SettingsClusterSyncEventSubscriber</c> forwards the change to every cluster peer.
///     <para>
///         Deliberately a small, focused helper rather than four near-identical copies of the
///         same six lines inline in each controller -- the same "author the shared piece once"
///         reasoning operational config sync's own generic envelope/applier/broadcaster design
///         already follows.
///     </para>
/// </summary>
/// <param name="stateRepository">The repository this class reads and writes a section's own sync state through.</param>
/// <param name="unitOfWork">The unit of work the state write is committed through.</param>
/// <param name="clusterOptions">This node's own cluster configuration, read for its own node id.</param>
/// <param name="eventPublisher">The event bus this class publishes a <see cref="ClusterConfigSettingsChangedEvent" /> through.</param>
public class ClusterConfigSyncSettingsRecorder(
    IClusterSyncedSettingsStateRepository stateRepository,
    IUnitOfWork unitOfWork,
    IOptionsMonitor<ClusterOptions> clusterOptions,
    IDnsServerEventPublisher eventPublisher
)
{
    /// <summary>
    ///     This method records a just-applied settings write and publishes the change for
    ///     cluster-wide propagation.
    /// </summary>
    /// <param name="sectionName">
    ///     The settings section's own stable name (for example <c>"EventBroker"</c>) -- must
    ///     match the matching settings controller's own <c>SectionName</c> constant exactly,
    ///     since this same value is <see cref="ClusterConfigSyncEnvelope.EntityKey" /> on the
    ///     receiving end.
    /// </param>
    /// <param name="settings">The exact configuration leaves that were just written.</param>
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
