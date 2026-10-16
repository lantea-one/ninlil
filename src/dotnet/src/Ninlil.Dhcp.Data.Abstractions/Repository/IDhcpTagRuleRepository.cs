using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dhcp.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with paged listing of all tag
///     rules (in the database) for <c>DhcpTagRulesController</c>, like
///     <see cref="IDhcpScopeRepository" /> (a rule need not belong to a scope, since
///     <see cref="DhcpTagRuleEntity.ScopeId" /> is optional), and the unpaged list of rules
///     eligible for a scope that the overlay services evaluate.
/// </summary>
public interface IDhcpTagRuleRepository : IRepository<DhcpTagRuleEntity>
{
    /// <summary>
    ///     This method lists one page of tag rules ordered by name, with sorting and paging in the
    ///     database.
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
    ///     This method counts all tag rules without loading them, for the total that accompanies
    ///     a <see cref="SearchAsync" /> page.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of tag rules.</returns>
    Task<int> CountAsync(CancellationToken stoppingToken = default);

    /// <summary>
    ///     This method lists every enabled tag rule eligible for a scope: those whose
    ///     <see cref="DhcpTagRuleEntity.ScopeId" /> is <paramref name="scopeId" /> or
    ///     <see langword="null" /> (global). It is unpaged, since every eligible rule is evaluated
    ///     for each request, and unsorted; the caller applies the order described on
    ///     <see cref="DhcpTagRuleEntity" />. Global rules are DHCPv4 rules, so the DHCPv6 overlay
    ///     keeps only the rules whose scope is <paramref name="scopeId" />.
    /// </summary>
    /// <param name="scopeId">The identifier of the scope a request was resolved into.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>Every enabled tag rule eligible for the scope.</returns>
    Task<IReadOnlyList<DhcpTagRuleEntity>> ListEnabledForScopeAsync(
        Guid scopeId,
        CancellationToken stoppingToken = default
    );
}
