using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dhcp.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with a tag rule's options:
///     paged listing (in the database) for the REST API, like
///     <see cref="IDhcpScopeCustomOptionRepository" />, and the unpaged list the overlay services
///     apply when a rule matches.
/// </summary>
public interface IDhcpTagRuleOptionRepository : IRepository<DhcpTagRuleOptionEntity>
{
    /// <summary>
    ///     This method lists one page of a rule's options, ordered by option tag, with sorting and
    ///     paging in the database.
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
    ///     This method counts a rule's options without loading them, for the total that
    ///     accompanies a <see cref="SearchByTagRuleIdAsync" /> page.
    /// </summary>
    /// <param name="tagRuleId">The identifier of the rule whose options should be counted.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of options the rule contributes.</returns>
    Task<int> CountByTagRuleIdAsync(Guid tagRuleId, CancellationToken stoppingToken = default);

    /// <summary>
    ///     This method lists all of a rule's options, unpaged. The overlay services call it when
    ///     the rule matches a request; the set is small.
    /// </summary>
    /// <param name="tagRuleId">The identifier of the rule whose options are needed.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>Every option the rule contributes.</returns>
    Task<IReadOnlyList<DhcpTagRuleOptionEntity>> ListByTagRuleIdAsync(
        Guid tagRuleId,
        CancellationToken stoppingToken = default
    );
}
