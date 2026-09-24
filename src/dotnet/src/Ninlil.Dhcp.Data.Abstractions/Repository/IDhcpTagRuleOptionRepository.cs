using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dhcp.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the real, pushed-down
///     paging a rule's own option list needs -- the same "scoped to a parent" shape
///     <see cref="IDhcpScopeCustomOptionRepository" /> already establishes for a scope's custom
///     options -- plus the unpaged lookup the option-delivery engine needs at real request time.
/// </summary>
public interface IDhcpTagRuleOptionRepository : IRepository<DhcpTagRuleOptionEntity>
{
    /// <summary>
    ///     This method lists one page of the options a rule contributes, ordered by option tag --
    ///     a real, translated <c>ORDER BY</c>/<c>OFFSET</c>/<c>LIMIT</c> (or the MongoDB
    ///     equivalent) scoped to <paramref name="tagRuleId" /> rather than an in-memory
    ///     sort/skip/take over the rule's full option set.
    /// </summary>
    /// <param name="tagRuleId">The identifier of the rule whose options should be listed.</param>
    /// <param name="skip">The number of matching options to skip, for paging.</param>
    /// <param name="take">The maximum number of options to return.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching page of options, ordered by option tag.</returns>
    Task<IReadOnlyList<DhcpTagRuleOptionEntity>> SearchByTagRuleIdAsync(
        Guid tagRuleId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method counts every option a rule contributes, without materializing them -- the
    ///     total <see cref="SearchByTagRuleIdAsync" />'s own paged response reports alongside its
    ///     current page.
    /// </summary>
    /// <param name="tagRuleId">The identifier of the rule whose options should be counted.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of options the rule contributes.</returns>
    Task<int> CountByTagRuleIdAsync(Guid tagRuleId, CancellationToken stoppingToken = default);

    /// <summary>
    ///     This method lists every option a rule contributes, unpaged -- used by the
    ///     option-delivery engine at real request time, where a rule's own full, realistically
    ///     small option set (not a paged, operator-facing listing) must be applied in full once the
    ///     rule is found to match.
    /// </summary>
    /// <param name="tagRuleId">The identifier of the rule whose options are needed.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>Every option the rule contributes.</returns>
    Task<IReadOnlyList<DhcpTagRuleOptionEntity>> ListByTagRuleIdAsync(
        Guid tagRuleId,
        CancellationToken stoppingToken = default
    );
}
