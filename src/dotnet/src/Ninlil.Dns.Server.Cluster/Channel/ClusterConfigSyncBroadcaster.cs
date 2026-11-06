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
///     This class implements <see cref="IClusterConfigSyncBroadcaster" />, the sending side of
///     config sync. The ClusterSync event subscribers call it with an envelope, which it sends,
///     with the signed, pinned client the heartbeat uses, to
///     <c>POST /api/cluster/channel/config-sync</c> on every
///     <see cref="ClusterNodeStatusEnum.Healthy" /> peer in parallel.
///     <para>
///         <b>Known limitation</b>: a peer that is <see cref="ClusterNodeStatusEnum.Unreachable" />
///         or <see cref="ClusterNodeStatusEnum.Joining" /> at that moment is skipped, and a failed
///         delivery is only logged. A broadcast has no retry or backlog. The one catch-up is the
///         join backfill (<see cref="Backfill.ClusterConfigBackfillRunner" />), which sends a
///         peer that just joined everything this node holds; a peer that misses a later broadcast
///         gets the entity again only when a later change to it is broadcast while the peer is
///         healthy, or when an operator intervenes.
///     </para>
///     <para>
///         The class also sends one envelope to one chosen peer (<see cref="SendToPeerAsync" />),
///         which is how a backfill delivers its items.
///     </para>
/// </summary>
/// <param name="scopeFactory">
///     The factory used to create a scope for each broadcast, since this singleton cannot hold
///     the scoped <see cref="IClusterNodeRepository" />.
/// </param>
/// <param name="httpClientFactory">The factory used to build and sign each outbound request.</param>
/// <param name="clusterOptions">The cluster settings.</param>
/// <param name="logger">The logger this class records a per-peer delivery failure to.</param>
public class ClusterConfigSyncBroadcaster(
    IServiceScopeFactory scopeFactory,
    ClusterPeerHttpClientFactory httpClientFactory,
    IOptionsMonitor<ClusterOptions> clusterOptions,
    ILogger<ClusterConfigSyncBroadcaster> logger
) : IClusterConfigSyncBroadcaster, IClusterConfigPeerSender
{
    /// <summary>
    ///     This constant contains the path of the <c>ClusterChannelController.ConfigSync</c> action.
    /// </summary>
    private const string ConfigSyncPath = "/api/cluster/channel/config-sync";

    /// <summary>
    ///     This field contains how long one delivery may take before it is abandoned: 10 seconds,
    ///     the default of <see cref="ClusterOptions.HeartbeatTimeout" />, which is ample for an
    ///     envelope.
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

        // Delivered concurrently, so one slow peer does not delay the others.
        await Task.WhenAll(
            healthyPeers.Select(peer =>
                DeliverToOneAsync(peer, requestBody, envelope, stoppingToken)
            )
        );
    }

    /// <inheritdoc />
    public async Task<bool> SendToPeerAsync(
        ClusterNodeEntity peer,
        ClusterConfigSyncEnvelope envelope,
        CancellationToken stoppingToken
    ) => await SendAsync(peer, JsonSerializer.Serialize(envelope), envelope, stoppingToken);

    /// <summary>
    ///     This method sends one serialized envelope to one peer and returns whether the peer
    ///     answered 2xx. It logs a rejection and lets a transport failure propagate.
    /// </summary>
    private async Task<bool> SendAsync(
        ClusterNodeEntity peer,
        string requestBody,
        ClusterConfigSyncEnvelope envelope,
        CancellationToken stoppingToken
    )
    {
        using HttpClient client = httpClientFactory.CreateClient(
            peer.TlsCertificateThumbprint,
            DeliveryTimeout
        );

        using HttpResponseMessage response = await httpClientFactory.SendSignedAsync(
            client,
            $"https://{peer.Host}:{peer.Port}",
            ConfigSyncPath,
            peer.TsigKeyName,
            requestBody,
            stoppingToken
        );

        if (response.IsSuccessStatusCode)
            return true;

        logger.LogWarning(
            "Peer node '{PeerName}' rejected a config-sync envelope for {EntityType} "
                + "'{EntityKey}' with HTTP {StatusCode}.",
            peer.Name,
            envelope.EntityType,
            envelope.EntityKey,
            (int)response.StatusCode
        );
        return false;
    }

    /// <summary>
    ///     This method delivers one envelope to one peer and logs a rejection or failure instead
    ///     of throwing (see the class summary), a timeout included.
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
            await SendAsync(peer, requestBody, envelope, stoppingToken);
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
                "Failed to deliver a config-sync envelope for {EntityType} '{EntityKey}' to "
                    + "peer node '{PeerName}'.",
                envelope.EntityType,
                envelope.EntityKey,
                peer.Name
            );
        }
    }
}
