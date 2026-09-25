using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the filtered, paged search
///     the admin console's log viewer needs. Paging through the whole table with
///     <see cref="IRepository{TEntity}.ListAsync" /> is impractical once a server has run for a
///     while.
/// </summary>
public interface ILogRepository : IRepository<LogEntity>
{
    /// <summary>
    ///     This method searches log entries in a time range, optionally narrowed by severity and
    ///     category, newest first.
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
