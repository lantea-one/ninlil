using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Cluster.Options;

// Define our namespace.
namespace Ninlil.Web.HealthChecks;

/// <summary>
///     This class implements the "cluster membership" health check, an addition to
///     <c>GET /status</c>'s existing checklist -- the aggregate, single-endpoint answer to "is
///     this node's own view of its cluster healthy right now," reusing the cluster's own peer registry
///     rather than probing anything itself (a fresh network probe here would duplicate
///     <c>ClusterHeartbeatBackgroundService</c>'s own already-running heartbeat engine, exactly
///     the same "reuse, don't re-check independently" reasoning
///     <see cref="UpstreamForwarderHealthCheck" /> already establishes for forwarder health).
///     When <see cref="ClusterOptions.Enable" /> is <see langword="false" />, this reports
///     <see cref="HealthStatus.Healthy" /> trivially -- clustering is a capability-expanding
///     toggle (invariant #6), not something whose absence is itself a problem.
/// </summary>
/// <param name="scopeFactory">The factory used to reach a scoped <see cref="IClusterNodeRepository" />.</param>
/// <param name="clusterOptions">This host's own cluster configuration, read live.</param>
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

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
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

        // Degraded, not unhealthy -- an unreachable peer never stops this node from answering
        // queries or serving its own locally-hosted zones; it only means cluster-wide sync to
        // that one peer is currently stalled (see ClusterConfigSyncBroadcaster's own doc comment
        // for the accepted "no retry/backfill" limitation this reflects).
        return HealthCheckResult.Degraded(
            $"{unreachablePeers.Count} of {peers.Count} known peer(s) are currently unreachable: "
                + $"{string.Join(", ", unreachablePeers)}."
        );
    }
}
