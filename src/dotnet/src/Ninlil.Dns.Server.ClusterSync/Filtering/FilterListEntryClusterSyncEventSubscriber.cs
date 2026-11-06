using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.ClusterSync.Shared;
using Ninlil.Dns.Server.Core.ClusterSync;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Filtering;

/// <summary>
///     This class is the sending-side subscriber for <see cref="DnsFilterListEntryChangedEvent" />
///     (see <see cref="ClusterConfigSyncSubscriberBase{TEntity}" /> for the shared flow).
/// </summary>
public class FilterListEntryClusterSyncEventSubscriber(
    IServiceScopeFactory scopeFactory,
    IClusterConfigSyncBroadcaster broadcaster,
    IOptionsMonitor<ClusterOptions> clusterOptions,
    ILogger<FilterListEntryClusterSyncEventSubscriber> logger
)
    : ClusterConfigSyncSubscriberBase<DnsFilterListEntryEntity>(
        scopeFactory,
        broadcaster,
        clusterOptions,
        logger
    ),
        IDnsServerEventSubscriber<DnsFilterListEntryChangedEvent>
{
    /// <inheritdoc />
    protected override string EntityType => FilterListEntryClusterConfigSyncApplier.EntityTypeName;

    /// <inheritdoc />
    public ValueTask HandleAsync(
        DnsFilterListEntryChangedEvent domainEvent,
        CancellationToken stoppingToken
    )
    {
        _ = BroadcastChangeAsync(
            domainEvent.EntryId,
            domainEvent.IsDeleted,
            domainEvent.OccurredAtUtc,
            stoppingToken
        );
        return ValueTask.CompletedTask;
    }
}
