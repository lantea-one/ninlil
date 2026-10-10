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
///     This class is the sending-side subscriber for <see cref="DnsRewriteChangedEvent" /> (see
///     <see cref="ClusterConfigSyncSubscriberBase{TEntity}" /> for the shared flow).
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

    /// <summary>
    ///     This method returns the rewrite, or <see langword="null" /> (nothing is broadcast) for
    ///     one scoped to a client or client group. <see cref="RewriteSyncPayload" /> has no scope
    ///     and clients and groups exist only on their own node, so a peer would create a global
    ///     rewrite from it.
    /// </summary>
    protected override async Task<DnsRewriteEntity> FindLocalAsync(
        IServiceProvider services,
        Guid id,
        CancellationToken stoppingToken
    )
    {
        DnsRewriteEntity rewrite = await services
            .GetRequiredService<IDnsRewriteRepository>()
            .GetByIdAsync(id, stoppingToken);

        return rewrite is { ClientId: null, ClientGroupId: null } ? rewrite : null;
    }

    /// <inheritdoc />
    protected override string Serialize(DnsRewriteEntity entity) =>
        JsonSerializer.Serialize(
            new RewriteSyncPayload(entity.Domain, entity.Target, entity.IsDisabled, entity.Comments)
        );
}
