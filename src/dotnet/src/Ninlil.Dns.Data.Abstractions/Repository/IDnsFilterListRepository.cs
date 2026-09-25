using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with a lookup of a filter list
///     by name and the paged listing behind <c>FilterListsController.List</c>, which has no
///     filters.
/// </summary>
public interface IDnsFilterListRepository : IRepository<DnsFilterListEntity>
{
    /// <summary>
    ///     This method looks up a list by its exact name.
    /// </summary>
    /// <param name="name">The list's name to look up.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching list, or <see langword="null" /> if none exists.</returns>
    Task<DnsFilterListEntity> FindByNameAsync(
        string name,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists one page of filter lists, ordered by name and paged in the database.
    /// </summary>
    /// <param name="skip">The number of matching lists to skip, for paging.</param>
    /// <param name="take">The maximum number of lists to return.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching page of lists, ordered by name.</returns>
    Task<IReadOnlyList<DnsFilterListEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method counts every filter list, for the total a paged response reports next to
    ///     <see cref="SearchAsync" />.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of filter lists.</returns>
    Task<int> CountAsync(CancellationToken stoppingToken = default);
}
