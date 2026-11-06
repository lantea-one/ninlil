using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the two reads the config
///     backfill needs: the request of one peer, and the peers that are owed a backfill. The
///     heartbeat uses the second after each round to decide which peers to start a pass for.
/// </summary>
public interface IClusterPeerConfigBackfillRepository : IRepository<ClusterPeerConfigBackfillEntity>
{
    /// <summary>
    ///     This method looks up the backfill request of one peer.
    /// </summary>
    /// <param name="clusterNodeId">
    ///     The identifier of the peer's local row (<see cref="ClusterNodeEntity" />).
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The request, or <see langword="null" /> if the peer is owed no backfill.</returns>
    Task<ClusterPeerConfigBackfillEntity> FindByClusterNodeIdAsync(
        Guid clusterNodeId,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists the identifiers of every peer that is owed a backfill. The count is
    ///     bounded by the number of peers, so the list stays small.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The peers' local row identifiers, in no particular order.</returns>
    Task<IReadOnlyList<Guid>> ListPendingClusterNodeIdsAsync(
        CancellationToken stoppingToken = default
    );
}
