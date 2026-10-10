using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.Abstractions.Routing;
using Ninlil.Dns.Server.Cluster.Options;

// Define our namespace.
namespace Ninlil.Web.HealthChecks;

/// <summary>
///     This class implements the <c>cluster</c> health check of <c>GET /status</c>: whether this
///     node sees its peers as reachable. It reads the peer statuses
///     <c>ClusterHeartbeatBackgroundService</c> records in the registry rather than probing peers
///     itself, which would duplicate the heartbeat (as <see cref="UpstreamForwarderHealthCheck" />
///     reuses forwarder health). With <see cref="ClusterOptions.Enable" /> off it reports
///     <see cref="HealthStatus.Healthy" />: clustering is optional, so its absence is not a
///     problem.
/// </summary>
/// <param name="scopeFactory">Opens a scope to reach the scoped <see cref="IClusterNodeRepository" />.</param>
/// <param name="clusterOptions">The cluster settings, read live.</param>
public sealed class ClusterHealthCheck(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<ClusterOptions> clusterOptions
) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        if (!clusterOptions.CurrentValue.Enable)
            return HealthCheckResult.Healthy("Clustering is disabled.");

        // Health is a read that tolerates replica lag; it reads the primary when no replica serves.
        await using AsyncServiceScope scope = scopeFactory.CreateReplicaReadScope();
        IClusterNodeRepository clusterNodeRepository =
            scope.ServiceProvider.GetRequiredService<IClusterNodeRepository>();

        IReadOnlyList<ClusterNodeEntity> peers = await clusterNodeRepository.ListAsync(
            stoppingToken: cancellationToken
        );

        if (peers.Count == 0)
            return HealthCheckResult.Healthy(
                "Clustering is enabled, but no peer has joined this node's own registry yet."
            );

        List<string> unreachablePeers =
        [
            .. peers
                .Where(peer => peer.Status == ClusterNodeStatusEnum.Unreachable)
                .Select(peer => peer.Name),
        ];

        if (unreachablePeers.Count == 0)
            return HealthCheckResult.Healthy(
                $"All {peers.Count} known peer(s) are reachable (healthy or still joining)."
            );

        // Degraded, not Unhealthy: an unreachable peer does not stop this node answering queries
        // or serving its zones; it only means sync to that peer is stalled, and changes made
        // meanwhile are not resent (ClusterConfigSyncBroadcaster has no retry or catch-up).
        return HealthCheckResult.Degraded(
            $"{unreachablePeers.Count} of {peers.Count} known peer(s) are currently unreachable: "
                + $"{string.Join(", ", unreachablePeers)}."
        );
    }
}
