using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookup the join
///     handshake and heartbeat need (a peer by the node identifier it reports, not the local row
///     identifier) and the paged listing behind <c>ClusterController.List</c>, which has no
///     filters.
/// </summary>
public interface IClusterNodeRepository : IRepository<ClusterNodeEntity>
{
    /// <summary>
    ///     This method looks up a peer by the stable node identifier it reports about itself.
    /// </summary>
    /// <param name="remoteNodeId">The peer's <c>ClusterOptions.NodeId</c> value.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching peer, or <see langword="null" /> if none is registered.</returns>
    Task<ClusterNodeEntity> FindByRemoteNodeIdAsync(
        Guid remoteNodeId,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists one page of the local peer registry, ordered by name and paged in the
    ///     database.
    /// </summary>
    /// <param name="skip">The number of matching peers to skip, for paging.</param>
    /// <param name="take">The maximum number of peers to return.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching page of peers, ordered by name.</returns>
    Task<IReadOnlyList<ClusterNodeEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method counts every peer in the local registry, for the total a paged response
    ///     reports next to <see cref="SearchAsync" />.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of registered peer nodes.</returns>
    Task<int> CountAsync(CancellationToken stoppingToken = default);
}
