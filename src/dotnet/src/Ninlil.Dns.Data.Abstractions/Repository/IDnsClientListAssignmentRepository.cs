using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookup the filtering
///     engine's client-derived matching needs: every list assigned directly to a single client --
///     plus the real, pushed-down paging <c>ClientListAssignmentsController</c>'s own <c>List</c>
///     action lists a client's own assignments through. Mirrors
///     <see cref="IDnsClientGroupListAssignmentRepository" /> exactly, scoped by
///     <see cref="DnsClientListAssignmentEntity.ClientId" /> instead of a group. Like
///     <see cref="IDnsClientGroupListAssignmentRepository" />, this repository takes no
///     search-criteria parameter beyond the client scope itself:
///     <c>ClientListAssignmentsController</c>'s own <c>List</c> action has never accepted a filter
///     querystring parameter of any kind, only pagination, so there is no real filter axis for
///     <see cref="SearchByClientIdAsync" /> to translate.
/// </summary>
public interface IDnsClientListAssignmentRepository : IRepository<DnsClientListAssignmentEntity>
{
    /// <summary>
    ///     This method lists every list assignment belonging to a single client -- the lookup
    ///     <c>DnsFilterEngine.ResolveApplicableListsAsync</c> uses to union a client's own direct
    ///     assignments into its resolved list set.
    /// </summary>
    /// <param name="clientId">The owning client's identifier.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching assignments.</returns>
    Task<IReadOnlyList<DnsClientListAssignmentEntity>> ListByClientIdAsync(
        Guid clientId,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists one page of the list assignments belonging to a single client,
    ///     ordered by creation time -- a real, translated <c>ORDER BY</c>/<c>OFFSET</c>/
    ///     <c>LIMIT</c> (or the MongoDB equivalent) scoped to <paramref name="clientId" /> rather
    ///     than an in-memory sort/skip/take over <see cref="ListByClientIdAsync" />'s own full
    ///     result.
    /// </summary>
    /// <param name="clientId">The owning client's identifier.</param>
    /// <param name="skip">The number of matching assignments to skip, for paging.</param>
    /// <param name="take">The maximum number of assignments to return.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching page of assignments, ordered by creation time.</returns>
    Task<IReadOnlyList<DnsClientListAssignmentEntity>> SearchByClientIdAsync(
        Guid clientId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method counts every list assignment belonging to a single client without
    ///     materializing them -- the total <see cref="SearchByClientIdAsync" />'s own paged
    ///     response reports alongside its current page.
    /// </summary>
    /// <param name="clientId">The owning client's identifier.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of list assignments belonging to the client.</returns>
    Task<int> CountByClientIdAsync(Guid clientId, CancellationToken stoppingToken = default);
}
