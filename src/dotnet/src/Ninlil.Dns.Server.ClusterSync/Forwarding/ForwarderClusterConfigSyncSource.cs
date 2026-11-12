using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.ClusterSync.Shared;
using Ninlil.Dns.Server.Core.ClusterSync;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Forwarding;

/// <summary>
///     This class is the <see cref="IClusterConfigSyncSource" /> for forwarders: every forwarder,
///     as <see cref="ForwarderClusterSyncEventSubscriber" /> sends it for a change. It is stage
///     <see cref="ClusterConfigBackfillStages.Independent" />.
/// </summary>
/// <param name="repository">The forwarder repository.</param>
/// <param name="clusterOptions">The cluster settings, read for this node's id.</param>
/// <param name="logger">The logger a skipped row is reported to.</param>
public sealed class ForwarderClusterConfigSyncSource(
    IDnsForwarderRepository repository,
    IOptionsMonitor<ClusterOptions> clusterOptions,
    ILogger<ForwarderClusterConfigSyncSource> logger
) : ClusterConfigSyncSourceBase<DnsForwarderEntity>(repository, clusterOptions, logger)
{
    /// <inheritdoc />
    public override string EntityType => ForwarderClusterConfigSyncApplier.EntityTypeName;

    /// <inheritdoc />
    public override int BackfillStage => ClusterConfigBackfillStages.Independent;

    /// <inheritdoc />
    protected override ValueTask<string> SerializeAsync(
        DnsForwarderEntity entity,
        CancellationToken stoppingToken
    ) =>
        ValueTask.FromResult(
            JsonSerializer.Serialize(
                new ForwarderSyncPayload(
                    entity.Name,
                    entity.DefinitionString,
                    entity.SortOrder,
                    entity.IsDisabled,
                    entity.Comments
                )
            )
        );
}
