using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookup the forwarding
///     resolver needs (every enabled global forwarder, in preference order) and the paged
///     listing behind <c>ForwardersController.List</c>, which has no filters.
/// </summary>
public interface IDnsForwarderRepository : IRepository<DnsForwarderEntity>
{
    /// <summary>
    ///     This method lists every enabled forwarder, ordered by
    ///     <see cref="DnsForwarderEntity.SortOrder" />.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The enabled forwarders, in preference order.</returns>
    Task<IReadOnlyList<DnsForwarderEntity>> ListEnabledOrderedAsync(
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists one page of global forwarders, enabled and disabled, ordered by
    ///     <see cref="DnsForwarderEntity.SortOrder" /> and then by name, paged in the database.
    /// </summary>
    /// <param name="skip">The number of matching forwarders to skip, for paging.</param>
    /// <param name="take">The maximum number of forwarders to return.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching page of forwarders, ordered by sort order and then by name.</returns>
    Task<IReadOnlyList<DnsForwarderEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method counts every global forwarder, for the total a paged response reports next
    ///     to <see cref="SearchAsync" />.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of global upstream forwarders.</returns>
    Task<int> CountAsync(CancellationToken stoppingToken = default);
}
