using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookups the filtering
///     engine's client-group matching needs, plus the real, pushed-down paging
///     <c>ClientGroupsController</c>'s own <c>List</c> action lists every group through. Like
///     <see cref="IZoneRepository" />, this repository takes no search-criteria parameter:
///     <c>ClientGroupsController</c>'s own <c>List</c> action has never accepted a filter
///     querystring parameter of any kind, only pagination, so there is no real filter axis for
///     <see cref="SearchAsync" /> to translate.
/// </summary>
public interface IDnsClientGroupRepository : IRepository<DnsClientGroupEntity>
{
    /// <summary>
    ///     This method looks up a client group by its exact name.
    /// </summary>
    /// <param name="name">The group's name to look up.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching group, or <see langword="null" /> if none exists.</returns>
    Task<DnsClientGroupEntity> FindByNameAsync(
        string name,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method looks up the fallback group a client matching no other group's own
    ///     networks is treated as belonging to.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     The group whose <see cref="DnsClientGroupEntity.IsDefault" /> is <see langword="true" />,
    ///     or <see langword="null" /> if none has been configured as the default yet.
    /// </returns>
    Task<DnsClientGroupEntity> GetDefaultAsync(CancellationToken stoppingToken = default);

    /// <summary>
    ///     This method lists one page of every client group, ordered by name -- a real,
    ///     translated <c>ORDER BY</c>/<c>OFFSET</c>/<c>LIMIT</c> (or the MongoDB equivalent)
    ///     rather than an in-memory sort/skip/take over <see cref="IRepository{TEntity}.ListAsync" />'s
    ///     own full result, matching the exact ordering <c>ClientGroupsController.List</c> already
    ///     applied in-memory before this method existed.
    /// </summary>
    /// <param name="skip">The number of matching groups to skip, for paging.</param>
    /// <param name="take">The maximum number of groups to return.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching page of groups, ordered by name.</returns>
    Task<IReadOnlyList<DnsClientGroupEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method counts every client group without materializing them -- the total
    ///     <see cref="SearchAsync" />'s own paged response reports alongside its current page.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of client groups on record.</returns>
    Task<int> CountAsync(CancellationToken stoppingToken = default);
}
