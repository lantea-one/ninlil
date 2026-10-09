using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the search and count behind
///     the query log's REST search endpoint, and the bulk deletes its retention cleanup uses. A
///     query log can reach millions of rows, so every method here must run as a filtered query
///     in the database (an indexed <c>WHERE</c> clause, or a MongoDB filter document), never a
///     full scan loaded into memory. It is the model for repositories whose list endpoint takes
///     filters (<see cref="DnsQueryLogSearchCriteria" />).
/// </summary>
public interface IDnsQueryLogRepository : IRepository<DnsQueryLogEntryEntity>
{
    /// <summary>
    ///     This method searches the query log with the filters in <paramref name="criteria" />,
    ///     newest entries first.
    /// </summary>
    /// <param name="criteria">The filters to apply; an empty instance matches everything.</param>
    /// <param name="skip">The number of matching entries to skip, for paging.</param>
    /// <param name="take">The maximum number of matching entries to return.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching entries, newest first.</returns>
    Task<IReadOnlyList<DnsQueryLogEntryEntity>> SearchAsync(
        DnsQueryLogSearchCriteria criteria,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method counts the entries matching <paramref name="criteria" />, for the total a
    ///     paged search response reports.
    /// </summary>
    /// <param name="criteria">The filters to apply; an empty instance matches everything.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The number of matching entries.</returns>
    Task<int> CountAsync(
        DnsQueryLogSearchCriteria criteria,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method deletes every entry older than <paramref name="cutoffUtc" /> as one bulk
    ///     delete, without loading the rows.
    /// </summary>
    /// <param name="cutoffUtc">The exclusive cutoff; an entry at or after this moment is kept.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The number of entries removed.</returns>
    Task<int> DeleteOlderThanAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method deletes every entry beyond the <paramref name="maxRetainedEntries" /> newest.
    ///     It is a size cap alongside the age-based <see cref="DeleteOlderThanAsync" />, because a
    ///     server with very high query volume could otherwise grow the log without bound within one
    ///     retention window. Newest means the greatest <c>OccurredAtUtc</c>; entries with the same
    ///     timestamp are ranked by the greatest <c>Id</c>. Implementations find the timestamp of
    ///     the first entry to drop and delete by that boundary, so the cost does not grow with the
    ///     number of entries kept and no list of identifiers is sent to the database (only the
    ///     entries tied at the boundary are listed).
    /// </summary>
    /// <param name="maxRetainedEntries">
    ///     The number of newest entries to keep; older entries beyond this count are removed. A
    ///     value of zero or less removes nothing.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The number of entries removed.</returns>
    Task<int> DeleteExcessAsync(int maxRetainedEntries, CancellationToken stoppingToken = default);

    /// <summary>
    ///     This method deletes every entry in the log, for an operator's "clear the log" action.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The number of entries removed.</returns>
    Task<int> ClearAsync(CancellationToken stoppingToken = default);

    /// <summary>
    ///     This method asks the engine to refresh the statistics its query planner uses after a
    ///     large delete. Only SQLite needs it (<c>PRAGMA optimize</c>); every other
    ///     implementation does nothing.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    Task OptimizeAsync(CancellationToken stoppingToken = default);
}
