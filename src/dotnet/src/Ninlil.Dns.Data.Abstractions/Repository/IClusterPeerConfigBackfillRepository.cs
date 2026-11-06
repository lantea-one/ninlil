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

    /// <summary>
    ///     This method removes the request of a peer only if its <c>RequestedAtUtc</c> still has
    ///     the given value, in one conditional statement that runs at once (it is not staged for
    ///     the unit of work). A pass that finishes calls it with the value it read when it
    ///     started. A request made during the pass moved that value, so the request is kept; a
    ///     read-compare-delete in the caller would lose a request that commits between the read
    ///     and the delete.
    /// </summary>
    /// <param name="clusterNodeId">The identifier of the peer's local row.</param>
    /// <param name="requestedAtUtc">The request time the caller read.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns><see langword="true" /> when the request was removed.</returns>
    Task<bool> RemoveIfUnchangedAsync(
        Guid clusterNodeId,
        DateTimeOffset requestedAtUtc,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method moves the request time of a peer's existing request to the given value and
    ///     leaves every other column alone, in one statement that runs at once. It is the
    ///     atomic form of "request a backfill again" and cannot fail on a concurrently removed row.
    /// </summary>
    /// <param name="clusterNodeId">The identifier of the peer's local row.</param>
    /// <param name="requestedAtUtc">The new request time.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns><see langword="true" /> when a request existed and was updated.</returns>
    Task<bool> TouchAsync(
        Guid clusterNodeId,
        DateTimeOffset requestedAtUtc,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method records one failed pass on a peer's existing request: it adds one to the
    ///     attempt count and sets the attempt time and the error, in one statement that runs at
    ///     once, so a request removed or touched meanwhile is not overwritten with a stale copy.
    /// </summary>
    /// <param name="clusterNodeId">The identifier of the peer's local row.</param>
    /// <param name="attemptedAtUtc">When the pass ended.</param>
    /// <param name="error">The reason, at most 1,000 characters.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns><see langword="true" /> when a request existed and was updated.</returns>
    Task<bool> RecordFailureAsync(
        Guid clusterNodeId,
        DateTimeOffset attemptedAtUtc,
        string error,
        CancellationToken stoppingToken = default
    );
}
