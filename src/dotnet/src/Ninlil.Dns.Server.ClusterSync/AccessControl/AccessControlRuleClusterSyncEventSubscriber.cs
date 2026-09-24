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
namespace Ninlil.Dns.Server.ClusterSync.AccessControl;

/// <summary>
///     This class is the sending-side subscriber for <see cref="DnsAccessControlRuleChangedEvent" />
///     -- see <see cref="ClusterConfigSyncSubscriberBase{TEntity}" /> for the shared broadcast
///     plumbing. Genuinely a second, independent subscriber to this same event alongside the
///     already-existing <c>DnsAccessControlRuleChangedEventSubscriber</c> (which reloads
///     <c>DnsAccessControlRuleCache</c>'s own in-memory snapshot) -- the event bus already
///     supports any number of subscribers per event type, so adding this one for Phase 9-6c
///     needed no change to that pre-existing subscriber at all.
/// </summary>
public class AccessControlRuleClusterSyncEventSubscriber(
    IServiceScopeFactory scopeFactory,
    IClusterConfigSyncBroadcaster broadcaster,
    IOptionsMonitor<ClusterOptions> clusterOptions,
    ILogger<AccessControlRuleClusterSyncEventSubscriber> logger
)
    : ClusterConfigSyncSubscriberBase<DnsAccessControlRuleEntity>(
        scopeFactory,
        broadcaster,
        clusterOptions,
        logger
    ),
        IDnsServerEventSubscriber<DnsAccessControlRuleChangedEvent>
{
    /// <inheritdoc />
    protected override string EntityType =>
        AccessControlRuleClusterConfigSyncApplier.EntityTypeName;

    /// <inheritdoc />
    public ValueTask HandleAsync(
        DnsAccessControlRuleChangedEvent domainEvent,
        CancellationToken stoppingToken
    )
    {
        _ = BroadcastChangeAsync(
            domainEvent.RuleId,
            domainEvent.IsDeleted,
            domainEvent.OccurredAtUtc,
            stoppingToken
        );
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    protected override async Task<DnsAccessControlRuleEntity> FindLocalAsync(
        IServiceProvider services,
        Guid id,
        CancellationToken stoppingToken
    ) =>
        await services
            .GetRequiredService<IDnsAccessControlRuleRepository>()
            .GetByIdAsync(id, stoppingToken);

    /// <inheritdoc />
    protected override string Serialize(DnsAccessControlRuleEntity entity) =>
        JsonSerializer.Serialize(
            new AccessControlRuleSyncPayload(
                entity.Name,
                entity.Cidr,
                entity.Capabilities,
                entity.IsDisabled
            )
        );
}
