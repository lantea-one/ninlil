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
///     This hosted service probes every known peer every <see cref="ClusterOptions.HeartbeatInterval" />
///     over the signed, TLS-pinned cluster channel, while clustering is enabled, and sets the
///     peer's <see cref="ClusterNodeEntity.Status" /> from whether this node's probe succeeded.
///     <para>
///         Health is what each node observes, not cluster-wide consensus: node A's view of B
///         reflects only A's probes to B. B probes A in the same way, so both directions are
///         checked, but the two registries can disagree for a while (see
///         <c>ClusterNodeStatusEnum</c>). That is acceptable for a small cluster.
///     </para>
/// </summary>
/// <param name="scopeFactory">
///     The factory used to create a scope for each round, for the scoped repository and unit of
///     work.
/// </param>
/// <param name="httpClientFactory">The factory used to build and sign each outbound probe.</param>
/// <param name="clusterOptions">The cluster settings, read on every round (except the interval).</param>
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
        // The interval is read once, at startup, so a change to Cluster:HeartbeatInterval needs a
        // restart (see ClusterSettingsController). The first round runs one interval after
        // startup.
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
                // A failed round (for example a database error) must not stop the service; the
                // next tick tries again. Status changes from the failed round are not saved.
                logger.LogWarning(exception, "A cluster heartbeat round failed unexpectedly.");
            }
        }
    }

    /// <summary>
    ///     This method waits for the timer's next tick and returns <see langword="false" /> instead
    ///     of throwing when the wait is cancelled, so the loop ends cleanly on shutdown.
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
    ///     This method probes every known peer once, in parallel, and saves the results together.
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

        // Probed concurrently, so a slow peer does not delay the others.
        await Task.WhenAll(peers.Select(peer => ProbeOneAsync(peer, options, stoppingToken)));

        foreach (ClusterNodeEntity peer in peers)
            clusterNodeRepository.Update(peer);

        await unitOfWork.SaveChangesAsync(stoppingToken);
    }

    /// <summary>
    ///     This method probes one peer and updates its <see cref="ClusterNodeEntity" /> in memory;
    ///     the caller saves all peers together. Any failure, a probe timeout included, is counted
    ///     as a missed heartbeat; only shutdown (a cancelled <paramref name="stoppingToken" />)
    ///     propagates.
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
        // An HttpClient timeout is a TaskCanceledException too; only a cancelled stoppingToken
        // (shutdown) propagates, so a peer that times out counts as failed like any other.
        catch (Exception exception)
            when (exception is not OperationCanceledException
                || !stoppingToken.IsCancellationRequested
            )
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
