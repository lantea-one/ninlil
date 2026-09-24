using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookups the join
///     handshake and heartbeat engine need: finding a peer by the stable node identifier it
///     asserts about itself, rather than only by this registry's own row identifier -- plus the
///     real, pushed-down paging <c>ClusterController</c>'s own <c>List</c> action lists the full
///     peer registry through. Like <see cref="IZoneRepository" />, this repository takes no
///     search-criteria parameter: <c>ClusterController</c>'s own <c>List</c> action has never
///     accepted a filter querystring parameter of any kind, only pagination, so there is no real
///     filter axis for <see cref="SearchAsync" /> to translate.
/// </summary>
public interface IClusterNodeRepository : IRepository<ClusterNodeEntity>
{
    /// <summary>
    ///     This method looks up a peer by its own self-asserted, stable node identifier.
    /// </summary>
    /// <param name="remoteNodeId">The peer's own <c>ClusterOptions.NodeId</c> value.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching peer, or <see langword="null" /> if none is registered.</returns>
    Task<ClusterNodeEntity> FindByRemoteNodeIdAsync(
        Guid remoteNodeId,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists one page of every peer node currently in this node's own registry,
    ///     ordered by name -- a real, translated <c>ORDER BY</c>/<c>OFFSET</c>/<c>LIMIT</c> (or the
    ///     MongoDB equivalent) rather than an in-memory sort/skip/take over a fully materialized
    ///     <see cref="IRepository{TEntity}.ListAsync" /> result.
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
    ///     This method counts every peer node currently in this node's own registry, without
    ///     materializing them -- the total <see cref="SearchAsync" />'s own paged response reports
    ///     alongside its current page.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of registered peer nodes.</returns>
    Task<int> CountAsync(CancellationToken stoppingToken = default);
}
