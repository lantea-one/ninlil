using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the bulk load
///     <c>DnsAccessControlRuleCache</c> needs for its in-memory snapshot and the paged listing
///     behind <c>DnsAccessControlRulesController.List</c>, which has no filters.
/// </summary>
public interface IDnsAccessControlRuleRepository : IRepository<DnsAccessControlRuleEntity>
{
    /// <summary>
    ///     This method lists every enabled rule: the set <c>DnsAccessControlRuleCache</c> loads at
    ///     startup and reloads whenever a <c>DnsAccessControlRuleChangedEvent</c> arrives. Leaving
    ///     disabled rules out here means the cache holds only rules that can grant a capability and
    ///     need not filter on every lookup.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>Every rule with <see cref="DnsAccessControlRuleEntity.IsDisabled" /> <see langword="false" />.</returns>
    Task<IReadOnlyList<DnsAccessControlRuleEntity>> ListEnabledAsync(
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists one page of access-control rules, enabled or not, ordered by name and
    ///     paged in the database.
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
    ///     This method counts every access-control rule, enabled or not, for the total a paged
    ///     response reports next to <see cref="SearchAsync" />.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of access-control rules.</returns>
    Task<int> CountAsync(CancellationToken stoppingToken = default);
}
