using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.Core.ClusterSync;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Settings;

/// <summary>
///     This class is the sending-side subscriber for <see cref="ClusterConfigSettingsChangedEvent" />.
///     Simpler than every other Phase 9-6c subscriber: a settings section has no database row to
///     re-read (its real content lives in the runtime JSON configuration file, written by
///     <c>Ninlil.Web</c>'s own <c>ClusterConfigSyncSettingsRecorder</c> before this event was
///     even published), so this class only ever needs to repackage what the event already carries
///     into a <see cref="ClusterConfigSyncEnvelope" /> and forward it -- no
///     <see cref="Microsoft.Extensions.DependencyInjection.IServiceScopeFactory" />, no
///     repository.
/// </summary>
/// <param name="broadcaster">The broadcaster this subscriber forwards a built envelope to.</param>
/// <param name="clusterOptions">This node's own cluster configuration, read for its own node id.</param>
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
    ///     This method does the actual broadcast, with every exception (other than cancellation)
    ///     caught and logged so a failure never propagates back into
    ///     <c>DnsServerEventDispatcher</c>.
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
