using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookup the
///     parental-control schedule engine needs (every schedule assigned to a client) and the
///     paged listing behind <c>ClientSchedulesController.List</c>, which is scoped to a client
///     and has no other filters. It has the same shape as
///     <see cref="IDnsClientListAssignmentRepository" />.
/// </summary>
public interface IDnsClientScheduleRepository : IRepository<DnsClientScheduleEntity>
{
    /// <summary>
    ///     This method lists every schedule of one client. <c>DnsClientScheduleEngine.EvaluateAsync</c>
    ///     uses it to check a resolved client's own schedules.
    /// </summary>
    /// <param name="clientId">The owning client's identifier.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching schedules.</returns>
    Task<IReadOnlyList<DnsClientScheduleEntity>> ListByClientIdAsync(
        Guid clientId,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists one page of one client's schedules, ordered by creation time and paged
    ///     in the database.
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
    ///     This method counts one client's schedules, for the total a paged response reports next to
    ///     <see cref="SearchByClientIdAsync" />.
    /// </summary>
    /// <param name="clientId">The owning client's identifier.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of schedules belonging to the client.</returns>
    Task<int> CountByClientIdAsync(Guid clientId, CancellationToken stoppingToken = default);
}
