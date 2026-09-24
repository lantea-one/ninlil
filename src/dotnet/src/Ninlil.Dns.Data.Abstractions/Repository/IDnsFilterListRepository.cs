using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookups the filtering
///     engine's matching logic and external-list refresh service need -- plus the real,
///     pushed-down paging <c>FilterListsController</c>'s own <c>List</c> action lists the full
///     list set through. Like <see cref="IZoneRepository" />, this repository takes no
///     search-criteria parameter: <c>FilterListsController</c>'s own <c>List</c> action has never
///     accepted a filter querystring parameter of any kind, only pagination, so there is no real
///     filter axis for <see cref="SearchAsync" /> to translate.
/// </summary>
public interface IDnsFilterListRepository : IRepository<DnsFilterListEntity>
{
    /// <summary>
    ///     This method looks up a list by its exact name.
    /// </summary>
    /// <param name="name">The list's name to look up.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching list, or <see langword="null" /> if none exists.</returns>
    Task<DnsFilterListEntity> FindByNameAsync(
        string name,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists one page of every block/allow list this server knows about, ordered
    ///     by name -- a real, translated <c>ORDER BY</c>/<c>OFFSET</c>/<c>LIMIT</c> (or the
    ///     MongoDB equivalent) rather than an in-memory sort/skip/take over a fully materialized
    ///     <see cref="IRepository{TEntity}.ListAsync" /> result.
    /// </summary>
    /// <param name="skip">The number of matching lists to skip, for paging.</param>
    /// <param name="take">The maximum number of lists to return.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching page of lists, ordered by name.</returns>
    Task<IReadOnlyList<DnsFilterListEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method counts every block/allow list this server knows about, without
    ///     materializing them -- the total <see cref="SearchAsync" />'s own paged response reports
    ///     alongside its current page.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of block/allow lists.</returns>
    Task<int> CountAsync(CancellationToken stoppingToken = default);
}
