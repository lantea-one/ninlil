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
namespace Ninlil.Dns.Server.ClusterSync.Rewrites;

/// <summary>
///     This class is the sending-side subscriber for <see cref="DnsRewriteChangedEvent" /> --
///     see <see cref="ClusterConfigSyncSubscriberBase{TEntity}" /> for the shared broadcast
///     plumbing.
/// </summary>
public class RewriteClusterSyncEventSubscriber(
    IServiceScopeFactory scopeFactory,
    IClusterConfigSyncBroadcaster broadcaster,
    IOptionsMonitor<ClusterOptions> clusterOptions,
    ILogger<RewriteClusterSyncEventSubscriber> logger
)
    : ClusterConfigSyncSubscriberBase<DnsRewriteEntity>(
        scopeFactory,
        broadcaster,
        clusterOptions,
        logger
    ),
        IDnsServerEventSubscriber<DnsRewriteChangedEvent>
{
    /// <inheritdoc />
    protected override string EntityType => RewriteClusterConfigSyncApplier.EntityTypeName;

    /// <inheritdoc />
    public ValueTask HandleAsync(
        DnsRewriteChangedEvent domainEvent,
        CancellationToken stoppingToken
    )
    {
        _ = BroadcastChangeAsync(
            domainEvent.RewriteId,
            domainEvent.IsDeleted,
            domainEvent.OccurredAtUtc,
            stoppingToken
        );
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    protected override async Task<DnsRewriteEntity> FindLocalAsync(
        IServiceProvider services,
        Guid id,
        CancellationToken stoppingToken
    ) => await services.GetRequiredService<IDnsRewriteRepository>().GetByIdAsync(id, stoppingToken);

    /// <inheritdoc />
    protected override string Serialize(DnsRewriteEntity entity) =>
        JsonSerializer.Serialize(
            new RewriteSyncPayload(entity.Domain, entity.Target, entity.IsDisabled, entity.Comments)
        );
}
