using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookup the
///     parental-control schedule engine's group-derived matching needs: every schedule assigned
///     to a single client group -- plus the real, pushed-down paging
///     <c>ClientGroupSchedulesController</c>'s own <c>List</c> action lists a group's own
///     schedules through. Mirrors <see cref="IDnsClientGroupListAssignmentRepository" /> exactly,
///     scoped by <see cref="DnsClientGroupScheduleEntity.ClientGroupId" /> instead of a
///     filter-list assignment.
/// </summary>
public interface IDnsClientGroupScheduleRepository : IRepository<DnsClientGroupScheduleEntity>
{
    /// <summary>
    ///     This method lists every schedule belonging to a single client group -- the lookup
    ///     <c>DnsClientScheduleEngine.EvaluateAsync</c> uses to check a resolved client's own
    ///     group's schedules.
    /// </summary>
    /// <param name="clientGroupId">The owning client group's identifier.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching schedules.</returns>
    Task<IReadOnlyList<DnsClientGroupScheduleEntity>> ListByClientGroupIdAsync(
        Guid clientGroupId,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists one page of the schedules belonging to a single client group,
    ///     ordered by creation time -- a real, translated <c>ORDER BY</c>/<c>OFFSET</c>/
    ///     <c>LIMIT</c> (or the MongoDB equivalent) scoped to <paramref name="clientGroupId" />
    ///     rather than an in-memory sort/skip/take over <see cref="ListByClientGroupIdAsync" />'s
    ///     own full result.
    /// </summary>
    /// <param name="clientGroupId">The owning client group's identifier.</param>
    /// <param name="skip">The number of matching schedules to skip, for paging.</param>
    /// <param name="take">The maximum number of schedules to return.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching page of schedules, ordered by creation time.</returns>
    Task<IReadOnlyList<DnsClientGroupScheduleEntity>> SearchByClientGroupIdAsync(
        Guid clientGroupId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method counts every schedule belonging to a single client group without
    ///     materializing them -- the total <see cref="SearchByClientGroupIdAsync" />'s own paged
    ///     response reports alongside its current page.
    /// </summary>
    /// <param name="clientGroupId">The owning client group's identifier.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of schedules belonging to the client group.</returns>
    Task<int> CountByClientGroupIdAsync(
        Guid clientGroupId,
        CancellationToken stoppingToken = default
    );
}
