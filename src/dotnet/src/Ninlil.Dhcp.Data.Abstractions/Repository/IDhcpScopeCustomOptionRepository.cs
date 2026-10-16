using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dhcp.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with a scope's custom options:
///     paged listing (in the database) for <c>DhcpScopeCustomOptionsController</c>, and the
///     unpaged list of enabled options for <c>Dhcpv4OptionOverlayService</c> and
///     <c>Dhcpv6OptionOverlayService</c>.
/// </summary>
public interface IDhcpScopeCustomOptionRepository : IRepository<DhcpScopeCustomOptionEntity>
{
    /// <summary>
    ///     This method lists one page of a scope's custom options, ordered by option tag, with
    ///     sorting and paging in the database.
    /// </summary>
    /// <param name="scopeId">The identifier of the scope whose custom options should be listed.</param>
    /// <param name="skip">The number of matching custom options to skip, for paging.</param>
    /// <param name="take">The maximum number of custom options to return.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching page of custom options, ordered by option tag.</returns>
    Task<IReadOnlyList<DhcpScopeCustomOptionEntity>> SearchByScopeIdAsync(
        Guid scopeId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method counts a scope's custom options without loading them, for the total that
    ///     accompanies a <see cref="SearchByScopeIdAsync" /> page.
    /// </summary>
    /// <param name="scopeId">The identifier of the scope whose custom options should be counted.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of custom options in the scope.</returns>
    Task<int> CountByScopeIdAsync(Guid scopeId, CancellationToken stoppingToken = default);

    /// <summary>
    ///     This method lists every enabled custom option of a scope, unpaged. The DHCPv4 overlay
    ///     service calls it for every reply; the set is small.
    /// </summary>
    /// <param name="scopeId">The identifier of the scope whose enabled custom options are needed.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>Every enabled custom option configured for the scope.</returns>
    Task<IReadOnlyList<DhcpScopeCustomOptionEntity>> ListEnabledByScopeIdAsync(
        Guid scopeId,
        CancellationToken stoppingToken = default
    );
}
