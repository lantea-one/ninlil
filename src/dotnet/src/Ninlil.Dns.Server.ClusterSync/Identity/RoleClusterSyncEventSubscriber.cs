using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.ClusterSync.Shared;
using Ninlil.Dns.Server.Core.ClusterSync;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Identity;

/// <summary>
///     This class is the sending-side subscriber for <see cref="RoleChangedEvent" />. It sends a
///     role's name, description and permissions to every healthy peer, like
///     <see cref="UserClusterSyncEventSubscriber" /> without the secrets; the payload is built by
///     <see cref="RoleClusterConfigSyncSource" /> (see
///     <see cref="ClusterConfigSyncSubscriberBase{TEntity}" /> for the shared flow).
/// </summary>
/// <param name="scopeFactory">The factory used to create a scope, and the source, per event.</param>
/// <param name="broadcaster">The broadcaster this subscriber forwards a built envelope to.</param>
/// <param name="clusterOptions">The cluster settings, read for this node's id.</param>
/// <param name="logger">The logger a broadcast failure is recorded to.</param>
public class RoleClusterSyncEventSubscriber(
    IServiceScopeFactory scopeFactory,
    IClusterConfigSyncBroadcaster broadcaster,
    IOptionsMonitor<ClusterOptions> clusterOptions,
    ILogger<RoleClusterSyncEventSubscriber> logger
)
    : ClusterConfigSyncSubscriberBase<RoleEntity>(
        scopeFactory,
        broadcaster,
        clusterOptions,
        logger
    ),
        IDnsServerEventSubscriber<RoleChangedEvent>
{
    /// <inheritdoc />
    protected override string EntityType => RoleClusterConfigSyncApplier.EntityTypeName;

    /// <inheritdoc />
    public ValueTask HandleAsync(RoleChangedEvent domainEvent, CancellationToken stoppingToken)
    {
        _ = BroadcastChangeAsync(
            domainEvent.RoleId,
            domainEvent.IsDeleted,
            domainEvent.OccurredAtUtc,
            stoppingToken
        );
        return ValueTask.CompletedTask;
    }
}
