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
///     This class implements <see cref="IClusterQueryStatsAggregator" />, the data source of the
///     cluster dashboard. Like <see cref="ClusterUsernameUniquenessChecker" />, it asks every
///     <see cref="ClusterNodeStatusEnum.Healthy" /> peer in parallel, here through
///     <c>POST /api/cluster/channel/query-stats</c>; a peer that is not healthy is reported
///     unreachable without a request.
/// </summary>
/// <param name="scopeFactory">
///     The factory used to create a scope for the peer-registry lookup, since this class is a
///     singleton like the other cluster-channel callers.
/// </param>
/// <param name="httpClientFactory">The factory used to build and sign each outbound request.</param>
/// <param name="clusterOptions">The cluster settings.</param>
/// <param name="logger">The logger a per-peer failure is recorded to.</param>
public class ClusterQueryStatsAggregator(
    IServiceScopeFactory scopeFactory,
    ClusterPeerHttpClientFactory httpClientFactory,
    IOptionsMonitor<ClusterOptions> clusterOptions,
    ILogger<ClusterQueryStatsAggregator> logger
) : IClusterQueryStatsAggregator
{
    /// <summary>
    ///     This constant contains the path of the <c>ClusterChannelController.QueryStats</c> action.
    /// </summary>
    private const string QueryStatsPath = "/api/cluster/channel/query-stats";

    /// <summary>
    ///     This field contains how long one peer may take to answer, so a dashboard load does not
    ///     hang on an unresponsive peer.
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

        // Queried concurrently, so one slow peer does not delay the others.
        ClusterPeerQueryStatsResult[] results = await Task.WhenAll(
            peers.Select(peer => CollectFromOnePeerAsync(peer, requestBody, stoppingToken))
        );

        return results;
    }

    /// <summary>
    ///     This method collects one peer's snapshot, reporting the peer unreachable when it is not
    ///     healthy, rejects the request, fails or times out.
    /// </summary>
    private async Task<ClusterPeerQueryStatsResult> CollectFromOnePeerAsync(
        ClusterNodeEntity peer,
        string requestBody,
        CancellationToken stoppingToken
    )
    {
        // A peer the heartbeat does not see as healthy is reported unreachable without waiting
        // for another timeout.
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
        // An HttpClient timeout is a TaskCanceledException too; only a cancelled stoppingToken
        // (shutdown) propagates, so a peer that times out counts as failed like any other.
        catch (Exception exception)
            when (exception is not OperationCanceledException
                || !stoppingToken.IsCancellationRequested
            )
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
