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
///     This class is the one real implementation of <see cref="IClusterUsernameUniquenessChecker" />
///     -- Phase 9-6d's own best-effort, cluster-wide username-uniqueness check. Reuses 9-6a's own
///     peer registry and TSIG-authenticated + TLS-encrypted HTTP client exactly the way
///     <see cref="ClusterConfigSyncBroadcaster" /> already does, querying
///     <c>POST /api/cluster/channel/username-check</c> on every currently
///     <see cref="ClusterNodeStatusEnum.Healthy" /> peer, in parallel.
/// </summary>
/// <param name="scopeFactory">
///     The factory used to create a fresh dependency injection scope for the peer-registry lookup
///     -- this class is registered as a singleton (its own caller, <c>UsersController</c>, is
///     scoped, but nothing here prevents a singleton registration, and matching
///     <see cref="ClusterConfigSyncBroadcaster" />'s own lifetime keeps both cluster-channel
///     callers consistent).
/// </param>
/// <param name="httpClientFactory">The factory used to build and sign each outbound request.</param>
/// <param name="clusterOptions">This node's own cluster configuration.</param>
/// <param name="logger">The logger a per-peer failure is recorded to.</param>
public class ClusterUsernameUniquenessChecker(
    IServiceScopeFactory scopeFactory,
    ClusterPeerHttpClientFactory httpClientFactory,
    IOptionsMonitor<ClusterOptions> clusterOptions,
    ILogger<ClusterUsernameUniquenessChecker> logger
) : IClusterUsernameUniquenessChecker
{
    /// <summary>
    ///     This constant contains the inbound path every peer's own <c>ClusterChannelController</c>
    ///     exposes for a username-uniqueness check -- see that controller's own
    ///     <c>UsernameCheck</c> action.
    /// </summary>
    private const string UsernameCheckPath = "/api/cluster/channel/username-check";

    /// <summary>
    ///     This constant contains how long a single peer's own response may take before it is
    ///     treated as unreachable and simply skipped -- deliberately short, since this check runs
    ///     synchronously inside a real user-creation REST call and must never make account
    ///     management feel slow because one peer is having a bad day.
    /// </summary>
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(3);

    /// <inheritdoc />
    public async Task<bool> ExistsOnAnyHealthyPeerAsync(
        string normalizedUserName,
        CancellationToken stoppingToken = default
    )
    {
        ClusterOptions options = clusterOptions.CurrentValue;
        if (!options.Enable)
            return false;

        using IServiceScope scope = scopeFactory.CreateScope();
        IClusterNodeRepository clusterNodeRepository =
            scope.ServiceProvider.GetRequiredService<IClusterNodeRepository>();

        IReadOnlyList<ClusterNodeEntity> healthyPeers = (
            await clusterNodeRepository.ListAsync(stoppingToken: stoppingToken)
        )
            .Where(peer => peer.Status == ClusterNodeStatusEnum.Healthy)
            .ToList();

        if (healthyPeers.Count == 0)
            return false;

        string requestBody = JsonSerializer.Serialize(
            new ClusterUsernameCheckRequest(normalizedUserName)
        );

        // Every peer is queried concurrently -- an unreachable or slow peer must never delay,
        // and must never be conflated with a peer that genuinely reports a collision.
        bool[] results = await Task.WhenAll(
            healthyPeers.Select(peer => CheckOnePeerAsync(peer, requestBody, stoppingToken))
        );

        return results.Any(exists => exists);
    }

    /// <summary>
    ///     This method queries one peer, returning <see langword="false" /> (never treated as a
    ///     collision) for any failure, timeout, or unreachable peer -- see this class's own doc
    ///     comment for why an unreachable peer must never be conflated with a genuine collision.
    /// </summary>
    private async Task<bool> CheckOnePeerAsync(
        ClusterNodeEntity peer,
        string requestBody,
        CancellationToken stoppingToken
    )
    {
        try
        {
            using HttpClient client = httpClientFactory.CreateClient(
                peer.TlsCertificateThumbprint,
                CheckTimeout
            );

            HttpResponseMessage response = await httpClientFactory.SendSignedAsync(
                client,
                $"https://{peer.Host}:{peer.Port}",
                UsernameCheckPath,
                peer.TsigKeyName,
                requestBody,
                stoppingToken
            );

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Peer node '{PeerName}' rejected a username-uniqueness check with HTTP "
                        + "{StatusCode} -- treated as 'unknown,' not as a collision.",
                    peer.Name,
                    (int)response.StatusCode
                );
                return false;
            }

            string responseBody = await response.Content.ReadAsStringAsync(stoppingToken);
            ClusterUsernameCheckResult result =
                JsonSerializer.Deserialize<ClusterUsernameCheckResult>(responseBody);

            return result?.Exists ?? false;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Failed to reach peer node '{PeerName}' for a username-uniqueness check -- "
                    + "treated as 'unknown,' not as a collision, per this project's own "
                    + "'never block user management on one unreachable peer' design.",
                peer.Name
            );
            return false;
        }
    }
}
