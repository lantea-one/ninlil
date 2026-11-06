using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.ClusterSync.Shared;
using Ninlil.Dns.Server.Core.ClusterSync;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Filtering;

/// <summary>
///     This class is the <see cref="IClusterConfigSyncSource" /> for filter list metadata:
///     subscribed lists and blocked-service lists as well as local ones, which is what
///     <see cref="FilterListClusterSyncEventSubscriber" /> sends for a change. A joining node
///     fetches the entries of a subscribed list itself, so only the list is sent. It is stage
///     <see cref="ClusterConfigBackfillStages.Foundations" />, because entries refer to a list.
/// </summary>
/// <param name="repository">The filter list repository.</param>
/// <param name="clusterOptions">The cluster settings, read for this node's id.</param>
/// <param name="logger">The logger a skipped row is reported to.</param>
public sealed class FilterListClusterConfigSyncSource(
    IDnsFilterListRepository repository,
    IOptionsMonitor<ClusterOptions> clusterOptions,
    ILogger<FilterListClusterConfigSyncSource> logger
) : ClusterConfigSyncSourceBase<DnsFilterListEntity>(repository, clusterOptions, logger)
{
    /// <inheritdoc />
    public override string EntityType => FilterListClusterConfigSyncApplier.EntityTypeName;

    /// <inheritdoc />
    public override int BackfillStage => ClusterConfigBackfillStages.Foundations;

    /// <inheritdoc />
    protected override ValueTask<string> SerializeAsync(
        DnsFilterListEntity entity,
        CancellationToken stoppingToken
    ) =>
        ValueTask.FromResult(
            JsonSerializer.Serialize(
                new FilterListSyncPayload(
                    entity.Name,
                    entity.Kind,
                    entity.Action,
                    entity.Source,
                    entity.SubscriptionUrl,
                    entity.RefreshInterval,
                    entity.IsDisabled,
                    entity.Comments,
                    entity.AppliesToAllClients
                )
            )
        );
}
