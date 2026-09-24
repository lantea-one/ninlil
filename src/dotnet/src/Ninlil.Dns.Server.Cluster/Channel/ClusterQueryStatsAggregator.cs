using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Cluster.Http;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.Core.ClusterSync;

// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Channel;

/// <summary>
///     This class is the one real implementation of <see cref="IClusterQueryStatsAggregator" />
///     -- Phase 9-6e's own aggregate cluster dashboard data source. Reuses 9-6a's own peer
///     registry and TSIG-authenticated + TLS-encrypted HTTP client exactly the way
///     <see cref="ClusterUsernameUniquenessChecker" /> already does, querying
///     <c>POST /api/cluster/channel/query-stats</c> on every currently
///     <see cref="ClusterNodeStatusEnum.Healthy" /> peer, in parallel; a peer that is not
///     currently healthy is reported unreachable immediately, with no network call at all.
/// </summary>
/// <param name="scopeFactory">
///     The factory used to create a fresh dependency injection scope for the peer-registry lookup
///     -- this class is registered as a singleton (its own caller, <c>ClusterController</c>, is
///     scoped, but nothing here prevents a singleton registration, and matching
///     <see cref="ClusterUsernameUniquenessChecker" />'s own lifetime keeps every cluster-channel
///     caller consistent).
/// </param>
/// <param name="httpClientFactory">The factory used to build and sign each outbound request.</param>
/// <param name="clusterOptions">This node's own cluster configuration.</param>
/// <param name="logger">The logger a per-peer failure is recorded to.</param>
public class ClusterQueryStatsAggregator(
    IServiceScopeFactory scopeFactory,
    ClusterPeerHttpClientFactory httpClientFactory,
    IOptionsMonitor<ClusterOptions> clusterOptions,
    ILogger<ClusterQueryStatsAggregator> logger
) : IClusterQueryStatsAggregator
{
    /// <summary>
    ///     This constant contains the inbound path every peer's own <c>ClusterChannelController</c>
    ///     exposes for a query-stats snapshot -- see that controller's own <c>QueryStats</c>
    ///     action.
    /// </summary>
    private const string QueryStatsPath = "/api/cluster/channel/query-stats";

    /// <summary>
    ///     This constant contains how long a single peer's own response may take before it is
    ///     treated as unreachable and simply omitted -- a dashboard load tolerates real latency far
    ///     better than a query answer ever could, but must still never hang indefinitely on one
    ///     unresponsive peer.
    /// </summary>
    private static readonly TimeSpan CollectionTimeout = TimeSpan.FromSeconds(5);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ClusterPeerQueryStatsResult>> CollectFromPeersAsync(
        CancellationToken stoppingToken = default
    )
    {
        ClusterOptions options = clusterOptions.CurrentValue;
        if (!options.Enable)
            return [];

        using IServiceScope scope = scopeFactory.CreateScope();
        IClusterNodeRepository clusterNodeRepository =
            scope.ServiceProvider.GetRequiredService<IClusterNodeRepository>();

        IReadOnlyList<ClusterNodeEntity> peers = await clusterNodeRepository.ListAsync(
            stoppingToken: stoppingToken
        );

        if (peers.Count == 0)
            return [];

        string requestBody = JsonSerializer.Serialize(new ClusterQueryStatsRequest(options.NodeId));

        // Every peer is queried concurrently -- one slow or unreachable peer must never delay
        // reporting for every other, reachable peer.
        ClusterPeerQueryStatsResult[] results = await Task.WhenAll(
            peers.Select(peer => CollectFromOnePeerAsync(peer, requestBody, stoppingToken))
        );

        return results;
    }

    /// <summary>
    ///     This method collects one peer's own snapshot, reporting it unreachable (never throwing)
    ///     for any peer not currently healthy, any failure, or any timeout.
    /// </summary>
    private async Task<ClusterPeerQueryStatsResult> CollectFromOnePeerAsync(
        ClusterNodeEntity peer,
        string requestBody,
        CancellationToken stoppingToken
    )
    {
        // A peer this node has not itself observed as healthy is reported unreachable without
        // ever attempting the network call -- its own heartbeat status already answered this
        // question, so there is no reason to wait out a fresh timeout against it here too.
        if (peer.Status != ClusterNodeStatusEnum.Healthy)
            return new ClusterPeerQueryStatsResult(peer.Id, peer.Name, false, null);

        try
        {
            using HttpClient client = httpClientFactory.CreateClient(
                peer.TlsCertificateThumbprint,
                CollectionTimeout
            );

            HttpResponseMessage response = await httpClientFactory.SendSignedAsync(
                client,
                $"https://{peer.Host}:{peer.Port}",
                QueryStatsPath,
                peer.TsigKeyName,
                requestBody,
                stoppingToken
            );

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Peer node '{PeerName}' rejected a query-stats request with HTTP "
                        + "{StatusCode} -- reported unreachable.",
                    peer.Name,
                    (int)response.StatusCode
                );
                return new ClusterPeerQueryStatsResult(peer.Id, peer.Name, false, null);
            }

            string responseBody = await response.Content.ReadAsStringAsync(stoppingToken);
            ClusterQueryStatsSnapshot snapshot =
                JsonSerializer.Deserialize<ClusterQueryStatsSnapshot>(responseBody);

            return new ClusterPeerQueryStatsResult(
                peer.Id,
                peer.Name,
                snapshot is not null,
                snapshot
            );
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Failed to reach peer node '{PeerName}' for a query-stats request -- reported "
                    + "unreachable.",
                peer.Name
            );
            return new ClusterPeerQueryStatsResult(peer.Id, peer.Name, false, null);
        }
    }
}
