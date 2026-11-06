using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Server.Core.ClusterSync;

// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Channel;

/// <summary>
///     This interface is the part of <see cref="ClusterConfigSyncBroadcaster" /> that sends one
///     config-sync envelope to one named peer. <see cref="Backfill.ClusterConfigBackfillRunner" />
///     calls it for every item of a backfill, and tests replace it with a fake so a pass runs
///     without a network.
/// </summary>
public interface IClusterConfigPeerSender
{
    /// <summary>
    ///     This method sends one envelope to one peer through the signed, pinned cluster channel,
    ///     whatever the peer's status. A rejection is logged by the implementation.
    /// </summary>
    /// <param name="peer">The peer to send to.</param>
    /// <param name="envelope">The envelope to send.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns><see langword="true" /> when the peer answered 2xx, <see langword="false" /> for any other status.</returns>
    /// <exception cref="HttpRequestException">Thrown when the peer cannot be reached.</exception>
    /// <exception cref="TaskCanceledException">Thrown when the delivery times out.</exception>
    Task<bool> SendToPeerAsync(
        ClusterNodeEntity peer,
        ClusterConfigSyncEnvelope envelope,
        CancellationToken stoppingToken
    );
}
