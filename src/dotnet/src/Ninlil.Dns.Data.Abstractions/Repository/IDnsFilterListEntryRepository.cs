using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookups the filtering
///     engine and the external-list refresh need, and the paged listing behind
///     <c>FilterListEntriesController.List</c>, which is scoped to a list and has no other
///     filters. A subscribed list can hold tens of thousands of entries, so database paging
///     matters more here than for most entities.
/// </summary>
public interface IDnsFilterListEntryRepository : IRepository<DnsFilterListEntryEntity>
{
    /// <summary>
    ///     This method lists every entry of one list.
    /// </summary>
    /// <param name="listId">The owning list's identifier.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching entries.</returns>
    Task<IReadOnlyList<DnsFilterListEntryEntity>> ListByListIdAsync(
        Guid listId,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists one page of one list's entries, ordered by value and paged in the
    ///     database. <c>DnsFilterListEntryConfiguration</c> defines an index on
    ///     <c>(ListId, Value)</c> so this query stays fast for large subscribed lists.
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
    ///     This method counts one list's entries, for the total a paged response reports next to
    ///     <see cref="SearchByListIdAsync" />.
    /// </summary>
    /// <param name="listId">The owning list's identifier.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of entries belonging to the list.</returns>
    Task<int> CountByListIdAsync(Guid listId, CancellationToken stoppingToken = default);

    /// <summary>
    ///     This method replaces every entry of one list with a new set, as a subscribed list's
    ///     refresh needs (a whole swap, not an entry-by-entry diff). On the relational engines the
    ///     old entries are deleted and the new ones inserted in the database at once, in bulk and
    ///     outside the change tracker, not at <see cref="IUnitOfWork.SaveChangesAsync" /> time, so
    ///     the caller must open a transaction first (<see cref="IUnitOfWorkTransactionFactory" />)
    ///     and commit it after its other changes are saved; otherwise a later failure leaves the
    ///     list replaced. MongoDB stages the delete and the insert together and runs them when
    ///     the unit of work saves.
    /// </summary>
    /// <param name="listId">The owning list's identifier.</param>
    /// <param name="entries">The complete new set of entries, each a value plus its exception flag.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    Task ReplaceEntriesAsync(
        Guid listId,
        IReadOnlyList<DnsFilterListReplacementEntry> entries,
        CancellationToken stoppingToken = default
    );
}
