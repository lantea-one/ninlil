using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookup the
///     filtering engine's client-group matching needs: every network a client group is matched
///     against -- plus the real, pushed-down paging <c>ClientGroupNetworksController</c>'s own
///     <c>List</c> action lists a group's own networks through. Like <see cref="IRecordRepository" />,
///     this repository takes no search-criteria parameter beyond the group scope itself:
///     <c>ClientGroupNetworksController</c>'s own <c>List</c> action has never accepted a filter
///     querystring parameter of any kind, only pagination, so there is no real filter axis for
///     <see cref="SearchByGroupIdAsync" /> to translate.
/// </summary>
public interface IDnsClientGroupNetworkRepository : IRepository<DnsClientGroupNetworkEntity>
{
    /// <summary>
    ///     This method lists every network belonging to a single client group.
    /// </summary>
    /// <param name="clientGroupId">The owning group's identifier.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching networks.</returns>
    Task<IReadOnlyList<DnsClientGroupNetworkEntity>> ListByGroupIdAsync(
        Guid clientGroupId,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists one page of the networks belonging to a single client group,
    ///     ordered by CIDR -- a real, translated <c>ORDER BY</c>/<c>OFFSET</c>/<c>LIMIT</c> (or
    ///     the MongoDB equivalent) scoped to <paramref name="clientGroupId" /> rather than an
    ///     in-memory sort/skip/take over <see cref="ListByGroupIdAsync" />'s own full result,
    ///     matching the exact ordering <c>ClientGroupNetworksController.List</c> already applied
    ///     in-memory before this method existed.
    /// </summary>
    /// <param name="clientGroupId">The owning group's identifier.</param>
    /// <param name="skip">The number of matching networks to skip, for paging.</param>
    /// <param name="take">The maximum number of networks to return.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching page of networks, ordered by CIDR.</returns>
    Task<IReadOnlyList<DnsClientGroupNetworkEntity>> SearchByGroupIdAsync(
        Guid clientGroupId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method counts every network belonging to a single client group without
    ///     materializing them -- the total <see cref="SearchByGroupIdAsync" />'s own paged
    ///     response reports alongside its current page.
    /// </summary>
    /// <param name="clientGroupId">The owning group's identifier.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of networks belonging to the group.</returns>
    Task<int> CountByGroupIdAsync(Guid clientGroupId, CancellationToken stoppingToken = default);
}
