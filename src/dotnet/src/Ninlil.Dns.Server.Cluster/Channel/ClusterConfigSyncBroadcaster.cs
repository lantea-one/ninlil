using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Cluster.Backfill;
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
///         <b>Missed deliveries</b>: a broadcast keeps no queue. A peer that is
///         <see cref="ClusterNodeStatusEnum.Unreachable" /> at that moment is skipped, and a
///         delivery that fails (a transport error, a timeout or a non-2xx answer) is logged. Both
///         record a backfill request for that peer through
///         <see cref="IClusterConfigBackfillRequester" />. The next heartbeat that reaches the
///         peer starts a backfill pass (<see cref="Backfill.ClusterConfigBackfillRunner" />), which
///         sends the peer everything this node holds, so the peer catches up on every change it
///         missed. A <see cref="ClusterNodeStatusEnum.Joining" /> peer is neither delivered to nor
///         recorded: the node has not yet probed it, and a request would push this node's own
///         accounts to it before the join backfill, which flows from the inviter to the joiner.
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
/// <param name="backfillRequester">
///     The recorder a skipped or failed delivery marks its peer with. It is the requester, not the
///     runner, because the runner sends through this class and a dependency on the runner would be
///     a cycle.
/// </param>
/// <param name="logger">The logger this class records a per-peer delivery failure to.</param>
public class ClusterConfigSyncBroadcaster(
    IServiceScopeFactory scopeFactory,
    ClusterPeerHttpClientFactory httpClientFactory,
    IOptionsMonitor<ClusterOptions> clusterOptions,
    IClusterConfigBackfillRequester backfillRequester,
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

        IReadOnlyList<ClusterNodeEntity> peers = await clusterNodeRepository.ListAsync(
            stoppingToken: stoppingToken
        );
        IReadOnlyList<ClusterNodeEntity> healthyPeers = peers
            .Where(peer => peer.Status == ClusterNodeStatusEnum.Healthy)
            .ToList();
        IReadOnlyList<ClusterNodeEntity> unreachablePeers = peers
            .Where(peer => peer.Status == ClusterNodeStatusEnum.Unreachable)
            .ToList();

        if (healthyPeers.Count == 0 && unreachablePeers.Count == 0)
            return;

        string requestBody = JsonSerializer.Serialize(envelope);

        // Delivered concurrently, so one slow peer does not delay the others. A skipped peer
        // only records its request; the requester answers a repeat from memory (see
        // ClusterConfigBackfillRequester), so a bulk change costs the database one write.
        await Task.WhenAll(
            healthyPeers
                .Select(peer => DeliverToOneAsync(peer, requestBody, envelope, stoppingToken))
                .Concat(unreachablePeers.Select(peer => RecordMissAsync(peer, stoppingToken)))
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
    ///     This method delivers one envelope to one peer and, on a rejection or failure, logs it
    ///     and records a backfill request instead of throwing (see the class summary), a timeout
    ///     included.
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
            if (!await SendAsync(peer, requestBody, envelope, stoppingToken))
                await RecordMissAsync(peer, stoppingToken);
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
            await RecordMissAsync(peer, stoppingToken);
        }
    }

    /// <summary>
    ///     This method marks a peer as owed a backfill because it missed a live change. A failure
    ///     to record is logged and swallowed: the broadcast must not throw into the event
    ///     subscriber that saved the change, and the next missed change tries again.
    /// </summary>
    private async Task RecordMissAsync(ClusterNodeEntity peer, CancellationToken stoppingToken)
    {
        try
        {
            await backfillRequester.RequestAsync(peer.Id, stoppingToken);
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException
                || !stoppingToken.IsCancellationRequested
            )
        {
            logger.LogWarning(
                exception,
                "Failed to record a config backfill request for peer node '{PeerName}'.",
                peer.Name
            );
        }
    }
}
