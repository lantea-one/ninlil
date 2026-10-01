using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.Core.ClusterSync;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Settings;

/// <summary>
///     This class is the sending-side subscriber for <see cref="ClusterConfigSettingsChangedEvent" />.
///     A settings section has no database row to read again: its content is in the runtime
///     configuration file, written before <c>ClusterConfigSyncSettingsRecorder</c> in
///     <c>Ninlil.Web</c> publishes the event, which carries the section JSON. So this class only
///     wraps the event in a <see cref="ClusterConfigSyncEnvelope" /> and broadcasts it, on a
///     detached task.
/// </summary>
/// <param name="broadcaster">The broadcaster this subscriber forwards a built envelope to.</param>
/// <param name="clusterOptions">The cluster settings, read for this node's id.</param>
/// <param name="logger">The logger a broadcast failure is recorded to.</param>
public class SettingsClusterSyncEventSubscriber(
    IClusterConfigSyncBroadcaster broadcaster,
    IOptionsMonitor<ClusterOptions> clusterOptions,
    ILogger<SettingsClusterSyncEventSubscriber> logger
) : IDnsServerEventSubscriber<ClusterConfigSettingsChangedEvent>
{
    /// <inheritdoc />
    public ValueTask HandleAsync(
        ClusterConfigSettingsChangedEvent domainEvent,
        CancellationToken stoppingToken
    )
    {
        _ = BroadcastAsync(domainEvent, stoppingToken);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    ///     This method broadcasts the envelope, logging any exception other than cancellation,
    ///     since nothing awaits it.
    /// </summary>
    private async Task BroadcastAsync(
        ClusterConfigSettingsChangedEvent domainEvent,
        CancellationToken stoppingToken
    )
    {
        try
        {
            await broadcaster.BroadcastAsync(
                new ClusterConfigSyncEnvelope(
                    SettingsClusterConfigSyncEntityType.EntityTypeName,
                    domainEvent.SectionName,
                    IsDeleted: false,
                    domainEvent.SettingsJson,
                    domainEvent.OccurredAtUtc,
                    clusterOptions.CurrentValue.NodeId
                ),
                stoppingToken
            );
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Unhandled exception while broadcasting a settings config-sync change for "
                    + "section '{SectionName}'.",
                domainEvent.SectionName
            );
        }
    }
}
