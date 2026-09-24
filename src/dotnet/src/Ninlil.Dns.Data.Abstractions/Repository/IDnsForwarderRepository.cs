using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookup the forwarding
///     resolver needs: every enabled global forwarder, in preference order -- plus the real,
///     pushed-down paging <c>ForwardersController</c>'s own <c>List</c> action lists the full
///     forwarder set through. Like <see cref="IZoneRepository" />, this repository takes no
///     search-criteria parameter: <c>ForwardersController</c>'s own <c>List</c> action has never
///     accepted a filter querystring parameter of any kind, only pagination, so there is no real
///     filter axis for <see cref="SearchAsync" /> to translate.
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
    ///     This method lists one page of every global upstream forwarder this server knows about
    ///     (enabled and disabled alike), ordered by <see cref="DnsForwarderEntity.SortOrder" /> and
    ///     then by name -- a real, translated <c>ORDER BY</c>/<c>OFFSET</c>/<c>LIMIT</c> (or the
    ///     MongoDB equivalent) rather than an in-memory sort/skip/take over a fully materialized
    ///     <see cref="IRepository{TEntity}.ListAsync" /> result, matching the exact ordering
    ///     <c>ForwardersController.List</c> already applied in-memory before this method existed.
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
    ///     This method counts every global upstream forwarder this server knows about, without
    ///     materializing them -- the total <see cref="SearchAsync" />'s own paged response reports
    ///     alongside its current page.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of global upstream forwarders.</returns>
    Task<int> CountAsync(CancellationToken stoppingToken = default);
}
