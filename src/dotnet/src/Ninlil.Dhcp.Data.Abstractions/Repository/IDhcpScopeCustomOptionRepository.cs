using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dhcp.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the real, pushed-down
///     paging a scope's own custom-option list needs -- the same "scoped to a parent" shape
///     <see cref="IDhcpReservationRepository" /> already establishes for a scope's reservations.
/// </summary>
public interface IDhcpScopeCustomOptionRepository : IRepository<DhcpScopeCustomOptionEntity>
{
    /// <summary>
    ///     This method lists one page of the custom options stored in a scope, ordered by option
    ///     tag -- a real, translated <c>ORDER BY</c>/<c>OFFSET</c>/<c>LIMIT</c> (or the MongoDB
    ///     equivalent) scoped to <paramref name="scopeId" /> rather than an in-memory
    ///     sort/skip/take over the scope's full custom-option set.
    /// </summary>
    /// <param name="scopeId">The identifier of the scope whose custom options should be listed.</param>
    /// <param name="skip">The number of matching custom options to skip, for paging.</param>
    /// <param name="take">The maximum number of custom options to return.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching page of custom options, ordered by option tag.</returns>
    Task<IReadOnlyList<DhcpScopeCustomOptionEntity>> SearchByScopeIdAsync(
        Guid scopeId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method counts every custom option stored in a scope, without materializing them --
    ///     the total <see cref="SearchByScopeIdAsync" />'s own paged response reports alongside its
    ///     current page.
    /// </summary>
    /// <param name="scopeId">The identifier of the scope whose custom options should be counted.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of custom options in the scope.</returns>
    Task<int> CountByScopeIdAsync(Guid scopeId, CancellationToken stoppingToken = default);

    /// <summary>
    ///     This method lists every enabled custom option configured for a scope, unpaged -- used
    ///     by the option-delivery engine at real request time, where a scope's own full,
    ///     realistically small custom-option set (not a paged, operator-facing listing) must be
    ///     consulted in full for every reply.
    /// </summary>
    /// <param name="scopeId">The identifier of the scope whose enabled custom options are needed.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>Every enabled custom option configured for the scope.</returns>
    Task<IReadOnlyList<DhcpScopeCustomOptionEntity>> ListEnabledByScopeIdAsync(
        Guid scopeId,
        CancellationToken stoppingToken = default
    );
}
