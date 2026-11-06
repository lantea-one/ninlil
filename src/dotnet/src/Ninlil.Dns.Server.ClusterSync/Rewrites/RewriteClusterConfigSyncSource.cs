using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.ClusterSync.Shared;
using Ninlil.Dns.Server.Core.ClusterSync;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Rewrites;

/// <summary>
///     This class is the <see cref="IClusterConfigSyncSource" /> for rewrites. Only global
///     rewrites sync: <see cref="RewriteSyncPayload" /> has no scope and clients and client groups
///     exist only on their own node, so a peer would turn a scoped rewrite into a global one. It
///     is stage <see cref="ClusterConfigBackfillStages.Independent" />.
/// </summary>
/// <param name="repository">The rewrite repository.</param>
/// <param name="clusterOptions">The cluster settings, read for this node's id.</param>
/// <param name="logger">The logger a skipped row is reported to.</param>
public sealed class RewriteClusterConfigSyncSource(
    IDnsRewriteRepository repository,
    IOptionsMonitor<ClusterOptions> clusterOptions,
    ILogger<RewriteClusterConfigSyncSource> logger
) : ClusterConfigSyncSourceBase<DnsRewriteEntity>(repository, clusterOptions, logger)
{
    /// <inheritdoc />
    public override string EntityType => RewriteClusterConfigSyncApplier.EntityTypeName;

    /// <inheritdoc />
    public override int BackfillStage => ClusterConfigBackfillStages.Independent;

    /// <inheritdoc />
    protected override Expression<Func<DnsRewriteEntity, bool>> SnapshotPredicate =>
        rewrite => rewrite.ClientId == null && rewrite.ClientGroupId == null;

    /// <inheritdoc />
    protected override ValueTask<string> SerializeAsync(
        DnsRewriteEntity entity,
        CancellationToken stoppingToken
    ) =>
        ValueTask.FromResult(
            JsonSerializer.Serialize(
                new RewriteSyncPayload(
                    entity.Domain,
                    entity.Target,
                    entity.IsDisabled,
                    entity.Comments
                )
            )
        );
}
