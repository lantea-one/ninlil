using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dhcp.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookup a DHCP server
///     needs to find the scope a request belongs to, plus the real, pushed-down paging a future
///     scope-listing REST action lists the full scope set through -- the same shape
///     <c>IZoneRepository</c> already establishes for zones, since a scope list is exactly the
///     same kind of "one flat, operator-managed set" as a zone list.
/// </summary>
public interface IDhcpScopeRepository : IRepository<DhcpScopeEntity>
{
    /// <summary>
    ///     This method looks up a scope by its exact, operator-chosen name.
    /// </summary>
    /// <param name="name">The scope name to look up.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching scope, or <see langword="null" /> if none exists.</returns>
    Task<DhcpScopeEntity> FindByNameAsync(string name, CancellationToken stoppingToken = default);

    /// <summary>
    ///     This method lists one page of every scope this server manages, ordered by name -- a
    ///     real, translated <c>ORDER BY</c>/<c>OFFSET</c>/<c>LIMIT</c> (or the MongoDB equivalent)
    ///     rather than an in-memory sort/skip/take over a fully materialized
    ///     <see cref="IRepository{TEntity}.ListAsync" /> result.
    /// </summary>
    /// <param name="skip">The number of matching scopes to skip, for paging.</param>
    /// <param name="take">The maximum number of scopes to return.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching page of scopes, ordered by name.</returns>
    Task<IReadOnlyList<DhcpScopeEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method counts every scope this server manages, without materializing them -- the
    ///     total <see cref="SearchAsync" />'s own paged response reports alongside its current
    ///     page.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of scopes.</returns>
    Task<int> CountAsync(CancellationToken stoppingToken = default);
}
