using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dhcp.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with a lookup by name and paged
///     listing (in the database) for <c>DhcpScopesController</c>, the same shape
///     <c>IZoneRepository</c> has for zones. The scope resolvers in <c>Ninlil.Dhcp.Server.Core</c>
///     list all scopes through <see cref="IRepository{TEntity}.ListAsync" />.
/// </summary>
public interface IDhcpScopeRepository : IRepository<DhcpScopeEntity>
{
    /// <summary>
    ///     This method looks up a scope by its exact name.
    /// </summary>
    /// <param name="name">The scope name to look up.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching scope, or <see langword="null" /> if none exists.</returns>
    Task<DhcpScopeEntity> FindByNameAsync(string name, CancellationToken stoppingToken = default);

    /// <summary>
    ///     This method lists one page of scopes ordered by name, with sorting and paging in the
    ///     database rather than over a full <see cref="IRepository{TEntity}.ListAsync" /> result.
    /// </summary>
    /// <param name="skip">The number of matching scopes to skip, for paging.</param>
    /// <param name="take">The maximum number of scopes to return.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching page of scopes, ordered by name.</returns>
    Task<IReadOnlyList<DhcpScopeEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method counts all scopes without loading them, for the total that accompanies a
    ///     <see cref="SearchAsync" /> page.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of scopes.</returns>
    Task<int> CountAsync(CancellationToken stoppingToken = default);
}
