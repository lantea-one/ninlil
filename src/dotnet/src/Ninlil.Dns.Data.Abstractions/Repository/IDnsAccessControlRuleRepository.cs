using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the one bulk lookup
///     <c>DnsAccessControlRuleCache</c> needs to load its entire in-memory
///     snapshot -- every other access pattern this rule type needs (a single lookup by
///     identifier for a REST <c>PUT</c>/<c>DELETE</c>) is already covered by
///     <see cref="IRepository{TEntity}" />'s own <c>GetByIdAsync</c> -- plus the real, pushed-down
///     paging <c>DnsAccessControlRulesController</c>'s own <c>List</c> action lists the full rule
///     set through. Like <see cref="IZoneRepository" />, this repository takes no
///     search-criteria parameter: <c>DnsAccessControlRulesController</c>'s own <c>List</c> action
///     has never accepted a filter querystring parameter of any kind, only pagination, so there
///     is no real filter axis for <see cref="SearchAsync" /> to translate.
/// </summary>
public interface IDnsAccessControlRuleRepository : IRepository<DnsAccessControlRuleEntity>
{
    /// <summary>
    ///     This method lists every currently-enabled rule -- the exact set
    ///     <c>DnsAccessControlRuleCache</c> loads into memory at startup and
    ///     reloads in full whenever a <c>DnsAccessControlRuleChangedEvent</c> arrives. Excluding
    ///     a disabled rule here, rather than filtering it out again on every cache lookup, keeps
    ///     the cache's own in-memory representation exactly the set of rules that can actually
    ///     grant a capability -- the same "never consulted while disabled" contract
    ///     <see cref="DnsAccessControlRuleEntity.IsDisabled" />'s own doc comment describes.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>Every rule with <see cref="DnsAccessControlRuleEntity.IsDisabled" /> <see langword="false" />.</returns>
    Task<IReadOnlyList<DnsAccessControlRuleEntity>> ListEnabledAsync(
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists one page of every access-control rule this server knows about,
    ///     enabled or not, ordered by name -- a real, translated <c>ORDER BY</c>/<c>OFFSET</c>/
    ///     <c>LIMIT</c> (or the MongoDB equivalent) rather than an in-memory sort/skip/take over a
    ///     fully materialized <see cref="IRepository{TEntity}.ListAsync" /> result.
    /// </summary>
    /// <param name="skip">The number of matching rules to skip, for paging.</param>
    /// <param name="take">The maximum number of rules to return.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching page of rules, ordered by name.</returns>
    Task<IReadOnlyList<DnsAccessControlRuleEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method counts every access-control rule this server knows about, enabled or not,
    ///     without materializing them -- the total <see cref="SearchAsync" />'s own paged
    ///     response reports alongside its current page.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of access-control rules.</returns>
    Task<int> CountAsync(CancellationToken stoppingToken = default);
}
