using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Cluster.Channel;
using Ninlil.Dns.Server.Cluster.Http;
using Ninlil.Dns.Server.Cluster.Options;

// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Heartbeat;

/// <summary>
///     This class is the real background heartbeat engine Phase 9-6a's own decision record calls
///     for: on a fixed interval (<see cref="ClusterOptions.HeartbeatInterval" />), it probes every
///     known peer over the TSIG-authenticated + TLS-encrypted cluster channel and updates that
///     peer's own <see cref="ClusterNodeEntity.Status" /> based purely on whether <em>this</em>
///     node's own probe succeeded.
///     <para>
///         This is a deliberately one-directional, locally-observed health model, not a
///         cluster-wide consensus: node A's own view of node B's health reflects only whether A's
///         own probes to B are currently succeeding, never anything B itself reports about A or
///         about a third node. Node B independently runs the exact same engine against A, so both
///         directions of the channel get probed, but the two nodes' own registries can briefly
///         disagree about a peer's status -- an accepted, documented simplification for this
///         project's homelab-scale deployment target (<c>ClusterNodeStatusEnum</c>'s own doc
///         comment covers this in more depth), the same kind of tradeoff already accepted
///         elsewhere in this project (the DNS-query rate limiter's own fail-open behavior, RFC
///         5011's partial state coverage).
///     </para>
/// </summary>
/// <param name="scopeFactory">
///     The factory used to create a fresh dependency injection scope for each heartbeat round,
///     rather than holding a single long-lived scope for this service's entire lifetime -- the
///     same pattern every other long-running hosted service in this project already follows for a
///     scoped repository/unit-of-work dependency.
/// </param>
/// <param name="httpClientFactory">The factory used to build and sign each outbound probe.</param>
/// <param name="clusterOptions">This node's own cluster configuration.</param>
/// <param name="logger">The logger this service records a status transition, or a probe failure, to.</param>
public class ClusterHeartbeatBackgroundService(
    IServiceScopeFactory scopeFactory,
    ClusterPeerHttpClientFactory httpClientFactory,
    IOptionsMonitor<ClusterOptions> clusterOptions,
    ILogger<ClusterHeartbeatBackgroundService> logger
) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The interval is read once, at startup -- like every other runtime settings write in
        // this project, a change to Cluster:HeartbeatInterval takes effect on the next restart,
        // not live; see ClusterController's own PATCH action doc comment.
        using PeriodicTimer timer = new(clusterOptions.CurrentValue.HeartbeatInterval);

        while (await WaitForNextTickAsync(timer, stoppingToken))
        {
            ClusterOptions options = clusterOptions.CurrentValue;
            if (!options.Enable)
                continue;

            try
            {
                await RunOneRoundAsync(options, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A single bad round (a transient database blip, for example) must never take
                // down this whole background service -- the next scheduled tick simply tries
                // again, the same resilience posture every other periodic hosted service in this
                // project already follows.
                logger.LogWarning(exception, "A cluster heartbeat round failed unexpectedly.");
            }
        }
    }

    /// <summary>
    ///     This method waits for the timer's next tick, translating the
    ///     <see cref="OperationCanceledException" /> a cancelled wait throws into a clean
    ///     <see langword="false" /> return instead, so the caller's own <see langword="while" />
    ///     loop can exit normally on shutdown rather than needing its own try/catch around every
    ///     iteration.
    /// </summary>
    private static async Task<bool> WaitForNextTickAsync(
        PeriodicTimer timer,
        CancellationToken stoppingToken
    )
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    ///     This method probes every currently known peer once, in parallel, and persists whatever
    ///     status changes resulted.
    /// </summary>
    private async Task RunOneRoundAsync(ClusterOptions options, CancellationToken stoppingToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        IClusterNodeRepository clusterNodeRepository =
            scope.ServiceProvider.GetRequiredService<IClusterNodeRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        IReadOnlyList<ClusterNodeEntity> peers = await clusterNodeRepository.ListAsync(
            stoppingToken: stoppingToken
        );

        if (peers.Count == 0)
            return;

        // Every peer is probed concurrently -- a slow or unreachable peer must never delay this
        // round's probe of every other, healthy peer.
        await Task.WhenAll(peers.Select(peer => ProbeOneAsync(peer, options, stoppingToken)));

        foreach (ClusterNodeEntity peer in peers)
            clusterNodeRepository.Update(peer);

        await unitOfWork.SaveChangesAsync(stoppingToken);
    }

    /// <summary>
    ///     This method probes a single peer and updates its own in-memory
    ///     <see cref="ClusterNodeEntity" /> instance in place -- the caller persists every peer's
    ///     own updated state together, once, after every probe in the round has finished.
    /// </summary>
    private async Task ProbeOneAsync(
        ClusterNodeEntity peer,
        ClusterOptions options,
        CancellationToken stoppingToken
    )
    {
        string requestBody = JsonSerializer.Serialize(
            new ClusterHeartbeatRequest(options.NodeId, DateTimeOffset.UtcNow)
        );

        try
        {
            using HttpClient client = httpClientFactory.CreateClient(
                peer.TlsCertificateThumbprint,
                options.HeartbeatTimeout
            );

            HttpResponseMessage response = await httpClientFactory.SendSignedAsync(
                client,
                $"https://{peer.Host}:{peer.Port}",
                "/api/cluster/channel/heartbeat",
                peer.TsigKeyName,
                requestBody,
                stoppingToken
            );

            if (response.IsSuccessStatusCode)
            {
                peer.LastHeartbeatAtUtc = DateTimeOffset.UtcNow;
                peer.MissedHeartbeatCount = 0;
                peer.Status = ClusterNodeStatusEnum.Healthy;
                return;
            }

            RecordMissedHeartbeat(peer, options, $"HTTP {(int)response.StatusCode}");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            RecordMissedHeartbeat(peer, options, exception.Message);
        }
    }

    /// <summary>
    ///     This method records one failed probe against a peer, moving it to
    ///     <see cref="ClusterNodeStatusEnum.Unreachable" /> once
    ///     <see cref="ClusterOptions.MissedHeartbeatThreshold" /> consecutive failures have
    ///     accumulated.
    /// </summary>
    private void RecordMissedHeartbeat(
        ClusterNodeEntity peer,
        ClusterOptions options,
        string reason
    )
    {
        peer.MissedHeartbeatCount++;

        if (peer.MissedHeartbeatCount >= options.MissedHeartbeatThreshold)
        {
            if (peer.Status != ClusterNodeStatusEnum.Unreachable)
                logger.LogWarning(
                    "Peer node '{PeerName}' ({PeerHost}:{PeerPort}) is now unreachable after {MissedCount} consecutive failed heartbeats: {Reason}",
                    peer.Name,
                    peer.Host,
                    peer.Port,
                    peer.MissedHeartbeatCount,
                    reason
                );

            peer.Status = ClusterNodeStatusEnum.Unreachable;
        }
    }
}
