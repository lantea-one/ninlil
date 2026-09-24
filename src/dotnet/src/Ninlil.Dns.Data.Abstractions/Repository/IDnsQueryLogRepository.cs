using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the real, pushed-down
///     search/count the query log's own REST search endpoint needs, and the bulk retention
///     deletes its own cleanup service needs. A query log can genuinely reach millions of rows on
///     a server that has been running for a long time, unlike this project's own much
///     lower-cardinality entities (zones, forwarders, and so on), where filtering the full,
///     already-fetched set in memory is an accepted tradeoff -- every method here is expected to
///     translate into a real, filtered query against the underlying storage engine (an indexed
///     <c>WHERE</c> clause for a relational provider; a filter document for MongoDB), never a
///     full table/collection scan materialized into memory first.
/// </summary>
public interface IDnsQueryLogRepository : IRepository<DnsQueryLogEntryEntity>
{
    /// <summary>
    ///     This method searches the query log, newest entries first, applying every filter
    ///     <paramref name="criteria" /> specifies.
    /// </summary>
    /// <param name="criteria">The filters to apply; an entirely empty instance matches everything.</param>
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
    ///     This method counts how many entries match every filter <paramref name="criteria" />
    ///     specifies, without materializing them -- the total a paginated search response reports
    ///     alongside its own current page.
    /// </summary>
    /// <param name="criteria">The filters to apply; an entirely empty instance matches everything.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The number of matching entries.</returns>
    Task<int> CountAsync(
        DnsQueryLogSearchCriteria criteria,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method permanently removes every entry older than <paramref name="cutoffUtc" />,
    ///     as a single bulk delete rather than loading each matching row into memory first only to
    ///     immediately discard it.
    /// </summary>
    /// <param name="cutoffUtc">The exclusive age cutoff; an entry at or after this moment survives.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The number of entries removed.</returns>
    Task<int> DeleteOlderThanAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method permanently removes every entry beyond the
    ///     <paramref name="maxRetainedEntries" /> newest ones, as a safety cap independent of
    ///     <see cref="DeleteOlderThanAsync" />'s own age-based retention -- a server answering an
    ///     unusually high query volume could otherwise still grow the log unboundedly within a
    ///     single retention window.
    /// </summary>
    /// <param name="maxRetainedEntries">
    ///     The number of newest entries to keep; every older entry beyond this count is removed.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The number of entries removed.</returns>
    Task<int> DeleteExcessAsync(int maxRetainedEntries, CancellationToken stoppingToken = default);

    /// <summary>
    ///     This method permanently removes every entry currently in the log, for an operator's
    ///     explicit "clear the log" action.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The number of entries removed.</returns>
    Task<int> ClearAsync(CancellationToken stoppingToken = default);
}
