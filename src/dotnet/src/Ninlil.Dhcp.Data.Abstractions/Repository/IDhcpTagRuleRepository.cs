using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dhcp.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the real, pushed-down
///     paging a future rule-listing REST action needs -- the same unscoped "one flat,
///     operator-managed set" shape <see cref="IDhcpScopeRepository" /> already establishes for
///     scopes, since a tag rule is not itself owned by exactly one scope (<see cref="DhcpTagRuleEntity.ScopeId" />
///     is optional) -- plus the unpaged lookup the option-delivery engine needs at real request
///     time.
/// </summary>
public interface IDhcpTagRuleRepository : IRepository<DhcpTagRuleEntity>
{
    /// <summary>
    ///     This method lists one page of every tag rule this server manages, ordered by name -- a
    ///     real, translated <c>ORDER BY</c>/<c>OFFSET</c>/<c>LIMIT</c> (or the MongoDB equivalent)
    ///     rather than an in-memory sort/skip/take over a fully materialized
    ///     <see cref="IRepository{TEntity}.ListAsync" /> result.
    /// </summary>
    /// <param name="skip">The number of matching rules to skip, for paging.</param>
    /// <param name="take">The maximum number of rules to return.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching page of rules, ordered by name.</returns>
    Task<IReadOnlyList<DhcpTagRuleEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method counts every tag rule this server manages, without materializing them --
    ///     the total <see cref="SearchAsync" />'s own paged response reports alongside its current
    ///     page.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of tag rules.</returns>
    Task<int> CountAsync(CancellationToken stoppingToken = default);

    /// <summary>
    ///     This method lists every enabled tag rule eligible for a specific scope -- one whose own
    ///     <see cref="DhcpTagRuleEntity.ScopeId" /> either names <paramref name="scopeId" /> exactly
    ///     or is <see langword="null" /> (a globally-eligible rule) -- unpaged, since the
    ///     option-delivery engine must evaluate every eligible rule for every request, not a page
    ///     of them. The result is not pre-sorted into evaluation order; the caller applies
    ///     <see cref="DhcpTagRuleEntity" />'s own documented priority/creation-time/identifier
    ///     ordering itself.
    /// </summary>
    /// <param name="scopeId">The identifier of the scope a request was resolved into.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>Every enabled tag rule eligible for the scope.</returns>
    Task<IReadOnlyList<DhcpTagRuleEntity>> ListEnabledForScopeAsync(
        Guid scopeId,
        CancellationToken stoppingToken = default
    );
}
