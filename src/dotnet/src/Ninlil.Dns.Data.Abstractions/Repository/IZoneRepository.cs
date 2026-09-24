using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookup a DNS server
///     needs on every single query it answers: given a name, find the zone that is authoritative
///     for it -- plus the real, pushed-down paging the <c>ZonesController</c> REST action lists
///     the full zone set through. Unlike <see cref="IDnsQueryLogRepository" />, this repository
///     takes no search-criteria parameter: <c>ZonesController</c>'s own <c>List</c> action has never
///     accepted a filter querystring parameter of any kind, only pagination, so there is no real
///     filter axis for <see cref="SearchAsync" /> to translate -- adding an empty placeholder
///     criteria type purely for shape-parity with the query log would be speculative complexity
///     with no caller. A future sub-phase that gives the zone list a real filter (by
///     <c>ZoneTypeEnum</c>, disabled status, or a name substring, say) should add a criteria
///     parameter to <see cref="SearchAsync" />/<see cref="CountAsync" /> at that point, the same
///     shape <see cref="IDnsQueryLogRepository" /> already establishes -- not before, since this
///     project's own convention is to build against a real, already-known caller rather than
///     anticipate one that does not exist yet.
/// </summary>
public interface IZoneRepository : IRepository<ZoneEntity>
{
    /// <summary>
    ///     This method looks up a zone by its exact, fully-qualified name.
    /// </summary>
    /// <param name="name">The zone name to look up, without a trailing dot.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching zone, or <see langword="null" /> if none exists.</returns>
    Task<ZoneEntity> FindByNameAsync(string name, CancellationToken stoppingToken = default);

    /// <summary>
    ///     This method lists one page of every zone this server hosts or references, ordered by
    ///     name -- a real, translated <c>ORDER BY</c>/<c>OFFSET</c>/<c>LIMIT</c> (or the MongoDB
    ///     equivalent) rather than an in-memory sort/skip/take over a fully materialized
    ///     <see cref="IRepository{TEntity}.ListAsync" /> result, so a large zone count never
    ///     forces the whole set into memory just to answer one page's worth of a request.
    /// </summary>
    /// <param name="skip">The number of matching zones to skip, for paging.</param>
    /// <param name="take">The maximum number of zones to return.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching page of zones, ordered by name.</returns>
    Task<IReadOnlyList<ZoneEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method counts every zone this server hosts or references, without materializing
    ///     them -- the total <see cref="SearchAsync" />'s own paged response reports alongside its
    ///     current page.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of zones.</returns>
    Task<int> CountAsync(CancellationToken stoppingToken = default);
}
