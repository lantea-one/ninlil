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
namespace Ninlil.Dns.Server.ClusterSync.Filtering;

/// <summary>
///     This class is the sending-side subscriber for <see cref="DnsFilterListChangedEvent" />,
///     sending block and allow-list metadata changes to every healthy peer (see
///     <see cref="ClusterConfigSyncSubscriberBase{TEntity}" /> for the shared flow).
/// </summary>
public class FilterListClusterSyncEventSubscriber(
    IServiceScopeFactory scopeFactory,
    IClusterConfigSyncBroadcaster broadcaster,
    IOptionsMonitor<ClusterOptions> clusterOptions,
    ILogger<FilterListClusterSyncEventSubscriber> logger
)
    : ClusterConfigSyncSubscriberBase<DnsFilterListEntity>(
        scopeFactory,
        broadcaster,
        clusterOptions,
        logger
    ),
        IDnsServerEventSubscriber<DnsFilterListChangedEvent>
{
    /// <inheritdoc />
    protected override string EntityType => FilterListClusterConfigSyncApplier.EntityTypeName;

    /// <inheritdoc />
    public ValueTask HandleAsync(
        DnsFilterListChangedEvent domainEvent,
        CancellationToken stoppingToken
    )
    {
        _ = BroadcastChangeAsync(
            domainEvent.ListId,
            domainEvent.IsDeleted,
            domainEvent.OccurredAtUtc,
            stoppingToken
        );
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    protected override async Task<DnsFilterListEntity> FindLocalAsync(
        IServiceProvider services,
        Guid id,
        CancellationToken stoppingToken
    ) =>
        await services
            .GetRequiredService<IDnsFilterListRepository>()
            .GetByIdAsync(id, stoppingToken);

    /// <inheritdoc />
    protected override string Serialize(DnsFilterListEntity entity) =>
        JsonSerializer.Serialize(
            new FilterListSyncPayload(
                entity.Name,
                entity.Kind,
                entity.Action,
                entity.Source,
                entity.SubscriptionUrl,
                entity.RefreshInterval,
                entity.IsDisabled,
                entity.Comments
            )
        );
}
