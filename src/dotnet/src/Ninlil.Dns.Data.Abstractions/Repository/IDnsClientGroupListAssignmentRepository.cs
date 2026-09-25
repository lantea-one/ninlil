using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookup the filtering
///     engine needs (every list assigned to a client group) and the paged listing behind
///     <c>ClientGroupListAssignmentsController.List</c>, which is scoped to a group and has no
///     other filters.
/// </summary>
public interface IDnsClientGroupListAssignmentRepository
    : IRepository<DnsClientGroupListAssignmentEntity>
{
    /// <summary>
    ///     This method lists every list assignment of one client group.
    /// </summary>
    /// <param name="clientGroupId">The owning group's identifier.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching assignments.</returns>
    Task<IReadOnlyList<DnsClientGroupListAssignmentEntity>> ListByGroupIdAsync(
        Guid clientGroupId,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists one page of one client group's list assignments, ordered by creation
    ///     time and paged in the database.
    /// </summary>
    /// <param name="clientGroupId">The owning group's identifier.</param>
    /// <param name="skip">The number of matching assignments to skip, for paging.</param>
    /// <param name="take">The maximum number of assignments to return.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching page of assignments, ordered by creation time.</returns>
    Task<IReadOnlyList<DnsClientGroupListAssignmentEntity>> SearchByGroupIdAsync(
        Guid clientGroupId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method counts one client group's list assignments, for the total a paged response
    ///     reports next to <see cref="SearchByGroupIdAsync" />.
    /// </summary>
    /// <param name="clientGroupId">The owning group's identifier.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of list assignments belonging to the group.</returns>
    Task<int> CountByGroupIdAsync(Guid clientGroupId, CancellationToken stoppingToken = default);
}
