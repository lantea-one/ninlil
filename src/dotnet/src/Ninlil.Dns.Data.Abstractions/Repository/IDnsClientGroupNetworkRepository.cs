using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookup the filtering
///     engine uses to match client groups (every rule of a group) and the paged listing behind
///     <c>ClientGroupNetworksController.List</c>, which is scoped to a group and has no other
///     filters.
/// </summary>
public interface IDnsClientGroupNetworkRepository : IRepository<DnsClientGroupNetworkEntity>
{
    /// <summary>
    ///     This method lists every match rule of one client group.
    /// </summary>
    /// <param name="clientGroupId">The owning group's identifier.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching networks.</returns>
    Task<IReadOnlyList<DnsClientGroupNetworkEntity>> ListByGroupIdAsync(
        Guid clientGroupId,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists one page of one client group's match rules, ordered by CIDR and paged
    ///     in the database.
    /// </summary>
    /// <param name="clientGroupId">The owning group's identifier.</param>
    /// <param name="skip">The number of matching networks to skip, for paging.</param>
    /// <param name="take">The maximum number of networks to return.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching page of networks, ordered by CIDR.</returns>
    Task<IReadOnlyList<DnsClientGroupNetworkEntity>> SearchByGroupIdAsync(
        Guid clientGroupId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method counts one client group's match rules, for the total a paged response
    ///     reports next to <see cref="SearchByGroupIdAsync" />.
    /// </summary>
    /// <param name="clientGroupId">The owning group's identifier.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of networks belonging to the group.</returns>
    Task<int> CountByGroupIdAsync(Guid clientGroupId, CancellationToken stoppingToken = default);
}
