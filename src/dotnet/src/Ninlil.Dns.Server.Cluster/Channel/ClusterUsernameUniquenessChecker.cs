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
///     This class implements <see cref="IClusterUsernameUniquenessChecker" />, the best-effort
///     cluster-wide username check made when a user is created. Like
///     <see cref="ClusterConfigSyncBroadcaster" />, it uses the peer registry and the signed,
///     pinned client, asking every <see cref="ClusterNodeStatusEnum.Healthy" /> peer in parallel
///     through <c>POST /api/cluster/channel/username-check</c>.
/// </summary>
/// <param name="scopeFactory">
///     The factory used to create a scope for the peer-registry lookup, since this class is a
///     singleton like the other cluster-channel callers.
/// </param>
/// <param name="httpClientFactory">The factory used to build and sign each outbound request.</param>
/// <param name="clusterOptions">The cluster settings.</param>
/// <param name="logger">The logger a per-peer failure is recorded to.</param>
public class ClusterUsernameUniquenessChecker(
    IServiceScopeFactory scopeFactory,
    ClusterPeerHttpClientFactory httpClientFactory,
    IOptionsMonitor<ClusterOptions> clusterOptions,
    ILogger<ClusterUsernameUniquenessChecker> logger
) : IClusterUsernameUniquenessChecker
{
    /// <summary>
    ///     This constant contains the path of the <c>ClusterChannelController.UsernameCheck</c>
    ///     action.
    /// </summary>
    private const string UsernameCheckPath = "/api/cluster/channel/username-check";

    /// <summary>
    ///     This field contains how long one peer may take to answer. It is short because the check
    ///     runs inside the user-creation request.
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

        // Queried concurrently, so one slow peer does not delay the others.
        bool[] results = await Task.WhenAll(
            healthyPeers.Select(peer => CheckOnePeerAsync(peer, requestBody, stoppingToken))
        );

        return results.Any(exists => exists);
    }

    /// <summary>
    ///     This method asks one peer, returning <see langword="false" /> for a rejection or
    ///     failure, so an unreachable peer never blocks user creation. A timeout is a
    ///     <see cref="TaskCanceledException" />, which the catch filter does not handle, so it
    ///     propagates.
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
