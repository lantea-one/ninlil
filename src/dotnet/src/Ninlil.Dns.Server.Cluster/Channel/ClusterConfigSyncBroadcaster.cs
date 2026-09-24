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
///     This class is the one real implementation of <see cref="IClusterConfigSyncBroadcaster" />
///     -- Phase 9-6c's sending half. It reuses Phase 9-6a's own peer registry and
///     TSIG-authenticated + TLS-encrypted HTTP client exactly the way
///     <c>ClusterHeartbeatBackgroundService</c> already does for a heartbeat probe, sending each
///     envelope to <c>POST /api/cluster/channel/config-sync</c> on every currently
///     <see cref="ClusterNodeStatusEnum.Healthy" /> peer, in parallel.
///     <para>
///         <b>A known, deliberately accepted limitation, not silently patched over</b>: a peer
///         that is <see cref="ClusterNodeStatusEnum.Unreachable" /> or <see cref="ClusterNodeStatusEnum.Joining" />
///         at broadcast time is skipped outright -- there is no retry queue, no backlog, and no
///         periodic reconciliation pass that would let it catch up once it becomes healthy again.
///         It will only ever converge if and when some <em>later</em> change to the very same
///         entity happens to reach it after it recovers, or via an operator's own manual
///         intervention. This is the same kind of homelab-scale tradeoff already accepted
///         elsewhere in this project (the DNS-query rate limiter's fail-open posture, RFC 5011's
///         partial state coverage) -- a full backfill/reconciliation mechanism is deliberately not
///         built; this is a known, accepted limitation.
///     </para>
/// </summary>
/// <param name="scopeFactory">
///     The factory used to create a fresh dependency injection scope for each broadcast, the
///     same pattern <see cref="Heartbeat.ClusterHeartbeatBackgroundService" /> and
///     <see cref="ClusterPeerHttpClientFactory" /> themselves already use for their own scoped
///     dependencies -- this class is registered as a singleton (every subscriber that calls it
///     is itself a singleton-lifetime event subscriber), so it cannot hold a scoped
///     <see cref="IClusterNodeRepository" /> directly.
/// </param>
/// <param name="httpClientFactory">The factory used to build and sign each outbound request.</param>
/// <param name="clusterOptions">This node's own cluster configuration.</param>
/// <param name="logger">The logger this class records a per-peer delivery failure to.</param>
public class ClusterConfigSyncBroadcaster(
    IServiceScopeFactory scopeFactory,
    ClusterPeerHttpClientFactory httpClientFactory,
    IOptionsMonitor<ClusterOptions> clusterOptions,
    ILogger<ClusterConfigSyncBroadcaster> logger
) : IClusterConfigSyncBroadcaster
{
    /// <summary>
    ///     This constant contains the inbound path every peer's own <c>ClusterChannelController</c>
    ///     exposes for a config-sync envelope -- see that controller's own <c>ConfigSync</c>
    ///     action.
    /// </summary>
    private const string ConfigSyncPath = "/api/cluster/channel/config-sync";

    /// <summary>
    ///     This constant contains how long a single peer's own delivery attempt may take before
    ///     it is abandoned -- the same generous default <see cref="ClusterOptions.HeartbeatTimeout" />
    ///     itself uses, since a config-sync payload is smaller and simpler than nothing this
    ///     timeout could not already comfortably cover.
    /// </summary>
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(10);

    /// <inheritdoc />
    public async Task BroadcastAsync(
        ClusterConfigSyncEnvelope envelope,
        CancellationToken stoppingToken
    )
    {
        ClusterOptions options = clusterOptions.CurrentValue;
        if (!options.Enable)
            return;

        using IServiceScope scope = scopeFactory.CreateScope();
        IClusterNodeRepository clusterNodeRepository =
            scope.ServiceProvider.GetRequiredService<IClusterNodeRepository>();

        IReadOnlyList<ClusterNodeEntity> healthyPeers = (
            await clusterNodeRepository.ListAsync(stoppingToken: stoppingToken)
        )
            .Where(peer => peer.Status == ClusterNodeStatusEnum.Healthy)
            .ToList();

        if (healthyPeers.Count == 0)
            return;

        string requestBody = JsonSerializer.Serialize(envelope);

        // Every peer is delivered to concurrently -- a slow or unreachable peer must never delay
        // delivery to any other, healthy peer, the same reasoning
        // ClusterHeartbeatBackgroundService.RunOneRoundAsync already establishes for a heartbeat
        // round.
        await Task.WhenAll(
            healthyPeers.Select(peer =>
                DeliverToOneAsync(peer, requestBody, envelope, stoppingToken)
            )
        );
    }

    /// <summary>
    ///     This method delivers one envelope to one peer, logging (rather than throwing) any
    ///     failure -- see this class's own doc comment for why a failed delivery is never
    ///     retried.
    /// </summary>
    private async Task DeliverToOneAsync(
        ClusterNodeEntity peer,
        string requestBody,
        ClusterConfigSyncEnvelope envelope,
        CancellationToken stoppingToken
    )
    {
        try
        {
            using HttpClient client = httpClientFactory.CreateClient(
                peer.TlsCertificateThumbprint,
                DeliveryTimeout
            );

            HttpResponseMessage response = await httpClientFactory.SendSignedAsync(
                client,
                $"https://{peer.Host}:{peer.Port}",
                ConfigSyncPath,
                peer.TsigKeyName,
                requestBody,
                stoppingToken
            );

            if (!response.IsSuccessStatusCode)
                logger.LogWarning(
                    "Peer node '{PeerName}' rejected a config-sync envelope for {EntityType} "
                        + "'{EntityKey}' with HTTP {StatusCode}.",
                    peer.Name,
                    envelope.EntityType,
                    envelope.EntityKey,
                    (int)response.StatusCode
                );
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Failed to deliver a config-sync envelope for {EntityType} '{EntityKey}' to "
                    + "peer node '{PeerName}'.",
                envelope.EntityType,
                envelope.EntityKey,
                peer.Name
            );
        }
    }
}
