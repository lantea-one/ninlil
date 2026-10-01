using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.ClusterSync.Shared;
using Ninlil.Dns.Server.Core.ClusterSync;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Forwarding;

/// <summary>
///     This class is the sending-side subscriber for <see cref="DnsForwarderChangedEvent" /> (see
///     <see cref="ClusterConfigSyncSubscriberBase{TEntity}" /> for the shared flow).
/// </summary>
public class ForwarderClusterSyncEventSubscriber(
    IServiceScopeFactory scopeFactory,
    IClusterConfigSyncBroadcaster broadcaster,
    IOptionsMonitor<ClusterOptions> clusterOptions,
    ILogger<ForwarderClusterSyncEventSubscriber> logger
)
    : ClusterConfigSyncSubscriberBase<DnsForwarderEntity>(
        scopeFactory,
        broadcaster,
        clusterOptions,
        logger
    ),
        IDnsServerEventSubscriber<DnsForwarderChangedEvent>
{
    /// <inheritdoc />
    protected override string EntityType => ForwarderClusterConfigSyncApplier.EntityTypeName;

    /// <inheritdoc />
    public ValueTask HandleAsync(
        DnsForwarderChangedEvent domainEvent,
        CancellationToken stoppingToken
    )
    {
        _ = BroadcastChangeAsync(
            domainEvent.ForwarderId,
            domainEvent.IsDeleted,
            domainEvent.OccurredAtUtc,
            stoppingToken
        );
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    protected override async Task<DnsForwarderEntity> FindLocalAsync(
        IServiceProvider services,
        Guid id,
        CancellationToken stoppingToken
    ) =>
        await services
            .GetRequiredService<IDnsForwarderRepository>()
            .GetByIdAsync(id, stoppingToken);

    /// <inheritdoc />
    protected override string Serialize(DnsForwarderEntity entity) =>
        JsonSerializer.Serialize(
            new ForwarderSyncPayload(
                entity.Name,
                entity.DefinitionString,
                entity.SortOrder,
                entity.IsDisabled,
                entity.Comments
            )
        );
}
