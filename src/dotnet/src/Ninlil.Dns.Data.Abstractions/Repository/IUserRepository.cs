using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookups an identity
///     provider needs to sign a <see cref="UserEntity" /> in: finding them by the name or e-mail
///     address they typed, rather than by the internal identifier they don't know -- plus the
///     real, pushed-down paging <c>UsersController</c>'s own <c>List</c> action lists every
///     account through. Like <see cref="IZoneRepository" />, this repository takes no
///     search-criteria parameter: <c>UsersController</c>'s own <c>List</c> action has never
///     accepted a filter querystring parameter of any kind, only pagination, so there is no real
///     filter axis for <see cref="SearchAsync" /> to translate.
/// </summary>
public interface IUserRepository : IRepository<UserEntity>
{
    /// <summary>
    ///     This method looks up a user by their normalized sign-in name.
    /// </summary>
    /// <param name="normalizedUserName">The upper-invariant sign-in name to look up.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching user, or <see langword="null" /> if none exists.</returns>
    Task<UserEntity> FindByNormalizedUserNameAsync(
        string normalizedUserName,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method looks up a user by their normalized e-mail address.
    /// </summary>
    /// <param name="normalizedEmail">The upper-invariant e-mail address to look up.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching user, or <see langword="null" /> if none exists.</returns>
    Task<UserEntity> FindByNormalizedEmailAsync(
        string normalizedEmail,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method looks up a user by the external identity provider's own immutable subject
    ///     identifier, rather than by anything the provider could reuse or reassign (such as an
    ///     e-mail address) -- the lookup <c>OidcUserProvisioningService</c> performs on every
    ///     completed OpenID Connect sign-in, both to find an already-provisioned account and to
    ///     decide whether one needs to be created.
    /// </summary>
    /// <param name="provider">
    ///     The external identity provider's own fixed name, matching
    ///     <see cref="UserEntity.ExternalIdentityProvider" />.
    /// </param>
    /// <param name="subjectId">
    ///     The provider's own <c>sub</c> claim value, matching <see cref="UserEntity.ExternalSubjectId" />.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching user, or <see langword="null" /> if none exists.</returns>
    Task<UserEntity> FindByExternalSubjectAsync(
        string provider,
        string subjectId,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists the roles held by a user.
    /// </summary>
    /// <param name="userId">The identifier of the user whose roles should be listed.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The roles the user currently holds.</returns>
    Task<IReadOnlyList<RoleEntity>> ListRolesAsync(
        Guid userId,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists the claims attached directly to a user, not counting the claims they
    ///     inherit from their roles.
    /// </summary>
    /// <param name="userId">The identifier of the user whose claims should be listed.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The claims attached directly to the user.</returns>
    Task<IReadOnlyList<UserClaimEntity>> ListClaimsAsync(
        Guid userId,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method grants a user a role.
    /// </summary>
    /// <param name="userId">The identifier of the user to grant the role to.</param>
    /// <param name="roleId">The identifier of the role to grant.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    Task AddToRoleAsync(Guid userId, Guid roleId, CancellationToken stoppingToken = default);

    /// <summary>
    ///     This method revokes a role from a user.
    /// </summary>
    /// <param name="userId">The identifier of the user to revoke the role from.</param>
    /// <param name="roleId">The identifier of the role to revoke.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    Task RemoveFromRoleAsync(Guid userId, Guid roleId, CancellationToken stoppingToken = default);

    /// <summary>
    ///     This method attaches one or more claims directly to a user.
    /// </summary>
    /// <param name="userId">The identifier of the user to attach the claims to.</param>
    /// <param name="claims">The type/value pairs to attach.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    Task AddClaimsAsync(
        Guid userId,
        IEnumerable<(string ClaimType, string ClaimValue)> claims,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method removes one or more claims that were attached directly to a user.
    /// </summary>
    /// <param name="userId">The identifier of the user to remove the claims from.</param>
    /// <param name="claims">The type/value pairs to remove.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    Task RemoveClaimsAsync(
        Guid userId,
        IEnumerable<(string ClaimType, string ClaimValue)> claims,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method replaces one claim attached directly to a user with another.
    /// </summary>
    /// <param name="userId">The identifier of the user whose claim should be replaced.</param>
    /// <param name="currentClaimType">The type of the claim to replace.</param>
    /// <param name="currentClaimValue">The value of the claim to replace.</param>
    /// <param name="newClaimType">The type of the replacement claim.</param>
    /// <param name="newClaimValue">The value of the replacement claim.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    Task ReplaceClaimAsync(
        Guid userId,
        string currentClaimType,
        string currentClaimValue,
        string newClaimType,
        string newClaimValue,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists every user who has a specific claim attached directly to them, not
    ///     counting a matching claim inherited from a role.
    /// </summary>
    /// <param name="claimType">The claim type to search for.</param>
    /// <param name="claimValue">The claim value to search for.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>Every user carrying a matching claim.</returns>
    Task<IReadOnlyList<UserEntity>> ListByClaimAsync(
        string claimType,
        string claimValue,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists one page of every user account, ordered by sign-in name -- a real,
    ///     translated <c>ORDER BY</c>/<c>OFFSET</c>/<c>LIMIT</c> (or the MongoDB equivalent)
    ///     rather than an in-memory sort/skip/take over <see cref="IRepository{TEntity}.ListAsync" />'s
    ///     own full result, matching the exact ordering <c>UsersController.List</c> already
    ///     applied in-memory before this method existed.
    /// </summary>
    /// <param name="skip">The number of matching accounts to skip, for paging.</param>
    /// <param name="take">The maximum number of accounts to return.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching page of accounts, ordered by sign-in name.</returns>
    Task<IReadOnlyList<UserEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method counts every user account without materializing them -- the total
    ///     <see cref="SearchAsync" />'s own paged response reports alongside its current page.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of user accounts on record.</returns>
    Task<int> CountAsync(CancellationToken stoppingToken = default);
}
