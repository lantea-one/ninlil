using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookups needed to
///     resolve a <see cref="RoleEntity" /> by name and to inspect the permission claims granted
///     to it -- plus the real, pushed-down paging <c>RolesController</c>'s own <c>List</c> action
///     lists every role through. Like <see cref="IZoneRepository" />, this repository takes no
///     search-criteria parameter: <c>RolesController</c>'s own <c>List</c> action has never
///     accepted a filter querystring parameter of any kind, only pagination, so there is no real
///     filter axis for <see cref="SearchAsync" /> to translate.
/// </summary>
public interface IRoleRepository : IRepository<RoleEntity>
{
    /// <summary>
    ///     This method looks up a role by its normalized name.
    /// </summary>
    /// <param name="normalizedName">The upper-invariant role name to look up.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching role, or <see langword="null" /> if none exists.</returns>
    Task<RoleEntity> FindByNormalizedNameAsync(
        string normalizedName,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists the claims granted to a role.
    /// </summary>
    /// <param name="roleId">The identifier of the role whose claims should be listed.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The claims granted to the role.</returns>
    Task<IReadOnlyList<RoleClaimEntity>> ListClaimsAsync(
        Guid roleId,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method grants a role a claim.
    /// </summary>
    /// <param name="roleId">The identifier of the role to grant the claim to.</param>
    /// <param name="claimType">The type of the claim to grant.</param>
    /// <param name="claimValue">The value of the claim to grant.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    Task AddClaimAsync(
        Guid roleId,
        string claimType,
        string claimValue,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method revokes a claim from a role.
    /// </summary>
    /// <param name="roleId">The identifier of the role to revoke the claim from.</param>
    /// <param name="claimType">The type of the claim to revoke.</param>
    /// <param name="claimValue">The value of the claim to revoke.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    Task RemoveClaimAsync(
        Guid roleId,
        string claimType,
        string claimValue,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists every user who holds a role, given the role's normalized name.
    /// </summary>
    /// <param name="normalizedRoleName">The upper-invariant role name to search for.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>Every user holding the matching role.</returns>
    Task<IReadOnlyList<UserEntity>> ListUsersInRoleAsync(
        string normalizedRoleName,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists one page of every role, ordered by name -- a real, translated
    ///     <c>ORDER BY</c>/<c>OFFSET</c>/<c>LIMIT</c> (or the MongoDB equivalent) rather than an
    ///     in-memory sort/skip/take over <see cref="IRepository{TEntity}.ListAsync" />'s own full
    ///     result, matching the exact ordering <c>RolesController.List</c> already applied
    ///     in-memory before this method existed.
    /// </summary>
    /// <param name="skip">The number of matching roles to skip, for paging.</param>
    /// <param name="take">The maximum number of roles to return.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching page of roles, ordered by name.</returns>
    Task<IReadOnlyList<RoleEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method counts every role without materializing them -- the total
    ///     <see cref="SearchAsync" />'s own paged response reports alongside its current page.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of roles on record.</returns>
    Task<int> CountAsync(CancellationToken stoppingToken = default);
}
