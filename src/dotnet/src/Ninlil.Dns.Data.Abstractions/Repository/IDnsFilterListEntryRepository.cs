using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookups the filtering
///     engine's matching logic and external-list refresh service need -- plus the real,
///     pushed-down paging <c>FilterListEntriesController</c>'s own <c>List</c> action lists a
///     single list's own entry set through. This is the one entity in this project's own
///     pagination sweep that can genuinely reach a large row count (a subscribed external block
///     list can carry tens of thousands of entries), so <see cref="SearchByListIdAsync" />'s own
///     real, translated <c>WHERE</c>/<c>ORDER BY</c>/<c>OFFSET</c>/<c>LIMIT</c> query matters more
///     here than for this project's other, lower-cardinality entities. Like
///     <see cref="IRecordRepository" />, this repository takes no search-criteria parameter
///     beyond the list scope itself: <c>FilterListEntriesController</c>'s own <c>List</c> action
///     has never accepted a filter querystring parameter of any kind, only pagination, so there
///     is no real filter axis for <see cref="SearchByListIdAsync" /> to translate.
/// </summary>
public interface IDnsFilterListEntryRepository : IRepository<DnsFilterListEntryEntity>
{
    /// <summary>
    ///     This method lists every entry belonging to a single list.
    /// </summary>
    /// <param name="listId">The owning list's identifier.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching entries.</returns>
    Task<IReadOnlyList<DnsFilterListEntryEntity>> ListByListIdAsync(
        Guid listId,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists one page of the entries belonging to a single list, ordered by
    ///     value -- a real, translated <c>WHERE</c>/<c>ORDER BY</c>/<c>OFFSET</c>/<c>LIMIT</c> (or
    ///     the MongoDB equivalent) scoped to <paramref name="listId" /> rather than an in-memory
    ///     sort/skip/take over <see cref="ListByListIdAsync" />'s own full result, matching the
    ///     exact ordering <c>FilterListEntriesController.List</c> already applied in-memory
    ///     before this method existed. The owning entity configuration
    ///     (<c>DnsFilterListEntryConfiguration</c>) carries a composite index on
    ///     <c>(ListId, Value)</c> specifically so this query is genuinely served by an index, not
    ///     merely syntactically pushed down, at the scale a subscribed list's entry set can reach.
    /// </summary>
    /// <param name="listId">The owning list's identifier.</param>
    /// <param name="skip">The number of matching entries to skip, for paging.</param>
    /// <param name="take">The maximum number of entries to return.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching page of entries, ordered by value.</returns>
    Task<IReadOnlyList<DnsFilterListEntryEntity>> SearchByListIdAsync(
        Guid listId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method counts every entry belonging to a single list, without materializing them
    ///     -- the total <see cref="SearchByListIdAsync" />'s own paged response reports alongside
    ///     its current page.
    /// </summary>
    /// <param name="listId">The owning list's identifier.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of entries belonging to the list.</returns>
    Task<int> CountByListIdAsync(Guid listId, CancellationToken stoppingToken = default);

    /// <summary>
    ///     This method atomically replaces every entry belonging to a single list with a freshly
    ///     supplied set -- what a subscribed list's refresh needs (a wholesale swap-in of a
    ///     newly fetched list, not an entry-by-entry diff), and what
    ///     <see cref="IUnitOfWork.SaveChangesAsync" /> then persists.
    /// </summary>
    /// <param name="listId">The owning list's identifier.</param>
    /// <param name="values">The complete new set of entry values.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    Task ReplaceEntriesAsync(
        Guid listId,
        IReadOnlyList<string> values,
        CancellationToken stoppingToken = default
    );
}
