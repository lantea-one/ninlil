using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookup the filtering
///     engine needs (every list assigned directly to a client) and the paged listing behind
///     <c>ClientListAssignmentsController.List</c>, which is scoped to a client and has no other
///     filters. It mirrors <see cref="IDnsClientGroupListAssignmentRepository" />, keyed by
///     <see cref="DnsClientListAssignmentEntity.ClientId" /> instead of a group.
/// </summary>
public interface IDnsClientListAssignmentRepository : IRepository<DnsClientListAssignmentEntity>
{
    /// <summary>
    ///     This method lists every list assignment of one client.
    ///     <c>DnsFilterEngine.ResolveApplicableListsAsync</c> adds them to the client's resolved
    ///     lists.
    /// </summary>
    /// <param name="clientId">The owning client's identifier.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching assignments.</returns>
    Task<IReadOnlyList<DnsClientListAssignmentEntity>> ListByClientIdAsync(
        Guid clientId,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists one page of one client's list assignments, ordered by creation time and
    ///     paged in the database.
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
    ///     This method counts one client's list assignments, for the total a paged response reports
    ///     next to <see cref="SearchByClientIdAsync" />.
    /// </summary>
    /// <param name="clientId">The owning client's identifier.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of list assignments belonging to the client.</returns>
    Task<int> CountByClientIdAsync(Guid clientId, CancellationToken stoppingToken = default);
}
