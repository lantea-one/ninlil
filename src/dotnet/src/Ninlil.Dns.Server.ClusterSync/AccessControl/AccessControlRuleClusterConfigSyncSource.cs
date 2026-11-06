using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.ClusterSync.Shared;
using Ninlil.Dns.Server.Core.ClusterSync;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.AccessControl;

/// <summary>
///     This class is the <see cref="IClusterConfigSyncSource" /> for access-control rules: every
///     rule, as <see cref="AccessControlRuleClusterSyncEventSubscriber" /> sends it for a change.
///     It is stage <see cref="ClusterConfigBackfillStages.Independent" />.
/// </summary>
/// <param name="repository">The access-control rule repository.</param>
/// <param name="clusterOptions">The cluster settings, read for this node's id.</param>
/// <param name="logger">The logger a skipped row is reported to.</param>
public sealed class AccessControlRuleClusterConfigSyncSource(
    IDnsAccessControlRuleRepository repository,
    IOptionsMonitor<ClusterOptions> clusterOptions,
    ILogger<AccessControlRuleClusterConfigSyncSource> logger
) : ClusterConfigSyncSourceBase<DnsAccessControlRuleEntity>(repository, clusterOptions, logger)
{
    /// <inheritdoc />
    public override string EntityType => AccessControlRuleClusterConfigSyncApplier.EntityTypeName;

    /// <inheritdoc />
    public override int BackfillStage => ClusterConfigBackfillStages.Independent;

    /// <inheritdoc />
    protected override ValueTask<string> SerializeAsync(
        DnsAccessControlRuleEntity entity,
        CancellationToken stoppingToken
    ) =>
        ValueTask.FromResult(
            JsonSerializer.Serialize(
                new AccessControlRuleSyncPayload(
                    entity.Name,
                    entity.Cidr,
                    entity.Capabilities,
                    entity.IsDisabled
                )
            )
        );
}
