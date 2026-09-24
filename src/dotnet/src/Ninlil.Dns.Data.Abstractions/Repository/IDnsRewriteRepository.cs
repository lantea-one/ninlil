using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookup the
///     query-time rewrite engine needs: every currently enabled rewrite -- plus the real,
///     pushed-down paging <c>DnsRewritesController</c>'s own <c>List</c> action lists the full
///     rewrite set through. No <c>FindByDomainAsync</c>-style lookup exists here -- unlike a TSIG
///     key's own unique <c>Name</c>, <see cref="DnsRewriteEntity.Domain" /> is deliberately
///     <b>not</b> unique (an operator may legitimately configure separate A-target and
///     AAAA-target rewrites for the same domain), so the engine's own match resolution always
///     works over the full, already-fetched enabled set rather than a single by-domain lookup.
///     Like <see cref="IZoneRepository" />, this repository takes no search-criteria parameter:
///     <c>DnsRewritesController</c>'s own <c>List</c> action has never accepted a filter
///     querystring parameter of any kind, only pagination, so there is no real filter axis for
///     <see cref="SearchAsync" /> to translate.
/// </summary>
public interface IDnsRewriteRepository : IRepository<DnsRewriteEntity>
{
    /// <summary>
    ///     This method lists every currently enabled rewrite -- the set
    ///     <c>Server.Rewrites</c>'s own <c>DnsRewriteEngine</c> matches a query against on every
    ///     call, the same "read fresh on every query, no in-memory cache of the set itself"
    ///     shape <c>ForwarderQueryResolver.ResolveForwarderSetAsync</c> already establishes for
    ///     <see cref="IDnsForwarderRepository.ListEnabledOrderedAsync" />.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    Task<IReadOnlyList<DnsRewriteEntity>> ListEnabledAsync(
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists one page of every DNS rewrite this server knows about, ordered by
    ///     domain and then by creation time -- a real, translated <c>ORDER BY</c>/<c>OFFSET</c>/
    ///     <c>LIMIT</c> (or the MongoDB equivalent) rather than an in-memory sort/skip/take over a
    ///     fully materialized <see cref="IRepository{TEntity}.ListAsync" /> result, matching the
    ///     exact ordering <c>DnsRewritesController.List</c> already applied in-memory before this
    ///     method existed.
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
    ///     This method counts every DNS rewrite this server knows about, without materializing
    ///     them -- the total <see cref="SearchAsync" />'s own paged response reports alongside
    ///     its current page.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of DNS rewrites.</returns>
    Task<int> CountAsync(CancellationToken stoppingToken = default);
}
