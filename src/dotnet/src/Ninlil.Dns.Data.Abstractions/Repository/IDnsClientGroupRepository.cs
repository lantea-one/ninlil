using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookups the filtering
///     engine uses to match client groups and the paged listing behind
///     <c>ClientGroupsController.List</c>, which has no filters.
/// </summary>
public interface IDnsClientGroupRepository : IRepository<DnsClientGroupEntity>
{
    /// <summary>
    ///     This method looks up a client group by its exact name.
    /// </summary>
    /// <param name="name">The group's name to look up.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching group, or <see langword="null" /> if none exists.</returns>
    Task<DnsClientGroupEntity> FindByNameAsync(
        string name,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method looks up the fallback group for clients that match no other group.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     The group whose <see cref="DnsClientGroupEntity.IsDefault" /> is <see langword="true" />,
    ///     or <see langword="null" /> if none has been configured as the default yet.
    /// </returns>
    Task<DnsClientGroupEntity> GetDefaultAsync(CancellationToken stoppingToken = default);

    /// <summary>
    ///     This method lists one page of client groups, ordered by name and paged in the database.
    /// </summary>
    /// <param name="skip">The number of matching groups to skip, for paging.</param>
    /// <param name="take">The maximum number of groups to return.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching page of groups, ordered by name.</returns>
    Task<IReadOnlyList<DnsClientGroupEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method counts every client group, for the total a paged response reports next to
    ///     <see cref="SearchAsync" />.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of client groups on record.</returns>
    Task<int> CountAsync(CancellationToken stoppingToken = default);
}
