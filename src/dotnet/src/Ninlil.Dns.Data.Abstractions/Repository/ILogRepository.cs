using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the filtered, paged
///     search that the administration console's log viewer needs; browsing the entire log table
///     a page at a time through the generic <see cref="IRepository{TEntity}.ListAsync" /> would
///     be impractical once a server has been running for any length of time.
/// </summary>
public interface ILogRepository : IRepository<LogEntity>
{
    /// <summary>
    ///     This method searches log entries within a time range, optionally narrowed further by
    ///     severity and/or category, returning the newest matches first.
    /// </summary>
    /// <param name="fromUtc">The inclusive start of the time range to search.</param>
    /// <param name="toUtc">The inclusive end of the time range to search.</param>
    /// <param name="minimumSeverity">
    ///     The lowest severity to include; entries less severe than this are excluded.
    /// </param>
    /// <param name="category">
    ///     An optional exact-match category filter; when <see langword="null" />, every category
    ///     is included.
    /// </param>
    /// <param name="skip">The number of matching entries to skip, for paging.</param>
    /// <param name="take">The maximum number of matching entries to return.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching log entries, newest first.</returns>
    Task<IReadOnlyList<LogEntity>> SearchAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        LogSeverityEnum minimumSeverity,
        string category,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    );
}
