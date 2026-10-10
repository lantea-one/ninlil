using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookup the rewrite
///     engine needs (every enabled rewrite) and the paged listing behind
///     <c>DnsRewritesController.List</c>, which has no filters. There is no lookup by domain:
///     <see cref="DnsRewriteEntity.Domain" /> is not unique (a domain can have both an A-target
///     and an AAAA-target rewrite), so the engine matches against the whole enabled set.
/// </summary>
public interface IDnsRewriteRepository : IRepository<DnsRewriteEntity>
{
    /// <summary>
    ///     This method lists every enabled rewrite: the set <c>Server.Rewrites.DnsRewriteEngine</c>
    ///     matches a query against. <c>Server.Rewrites.DnsRewriteCache</c> reads it once into memory and
    ///     again after a saved rewrite or when its time-to-live ends, not on each query.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    Task<IReadOnlyList<DnsRewriteEntity>> ListEnabledAsync(
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists one page of DNS rewrites, ordered by domain and then by creation time,
    ///     paged in the database.
    /// </summary>
    /// <param name="skip">The number of matching rewrites to skip, for paging.</param>
    /// <param name="take">The maximum number of rewrites to return.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching page of rewrites, ordered by domain and then by creation time.</returns>
    Task<IReadOnlyList<DnsRewriteEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method counts every DNS rewrite, for the total a paged response reports next to
    ///     <see cref="SearchAsync" />.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of DNS rewrites.</returns>
    Task<int> CountAsync(CancellationToken stoppingToken = default);
}
