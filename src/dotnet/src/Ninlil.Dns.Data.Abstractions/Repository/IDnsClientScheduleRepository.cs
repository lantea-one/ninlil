using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookup the
///     parental-control schedule engine's client-derived matching needs: every schedule assigned
///     directly to a single client -- plus the real, pushed-down paging
///     <c>ClientSchedulesController</c>'s own <c>List</c> action lists a client's own schedules
///     through. Mirrors <see cref="IDnsClientListAssignmentRepository" /> exactly, scoped by
///     <see cref="DnsClientScheduleEntity.ClientId" /> instead of a filter-list assignment; like
///     that repository, this one takes no search-criteria parameter beyond the client scope
///     itself, since <c>ClientSchedulesController</c>'s own <c>List</c> action has never accepted
///     a filter querystring parameter of any kind, only pagination.
/// </summary>
public interface IDnsClientScheduleRepository : IRepository<DnsClientScheduleEntity>
{
    /// <summary>
    ///     This method lists every schedule belonging to a single client -- the lookup
    ///     <c>DnsClientScheduleEngine.EvaluateAsync</c> uses to check a resolved client's own
    ///     direct schedules.
    /// </summary>
    /// <param name="clientId">The owning client's identifier.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching schedules.</returns>
    Task<IReadOnlyList<DnsClientScheduleEntity>> ListByClientIdAsync(
        Guid clientId,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists one page of the schedules belonging to a single client, ordered by
    ///     creation time -- a real, translated <c>ORDER BY</c>/<c>OFFSET</c>/<c>LIMIT</c> (or the
    ///     MongoDB equivalent) scoped to <paramref name="clientId" /> rather than an in-memory
    ///     sort/skip/take over <see cref="ListByClientIdAsync" />'s own full result.
    /// </summary>
    /// <param name="clientId">The owning client's identifier.</param>
    /// <param name="skip">The number of matching schedules to skip, for paging.</param>
    /// <param name="take">The maximum number of schedules to return.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching page of schedules, ordered by creation time.</returns>
    Task<IReadOnlyList<DnsClientScheduleEntity>> SearchByClientIdAsync(
        Guid clientId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method counts every schedule belonging to a single client without materializing
    ///     them -- the total <see cref="SearchByClientIdAsync" />'s own paged response reports
    ///     alongside its current page.
    /// </summary>
    /// <param name="clientId">The owning client's identifier.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of schedules belonging to the client.</returns>
    Task<int> CountByClientIdAsync(Guid clientId, CancellationToken stoppingToken = default);
}
