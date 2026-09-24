using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IUserRepository" /> against
///     <see cref="NinlilDnsContext" />.
/// </summary>
/// <param name="context">The database context to operate against.</param>
public class UserRepository(NinlilDnsContext context)
    : EntityFrameworkRepositoryBase<UserEntity>(context),
        IUserRepository
{
    /// <inheritdoc />
    public Task<UserEntity> FindByNormalizedUserNameAsync(
        string normalizedUserName,
        CancellationToken stoppingToken = default
    ) =>
        Set.FirstOrDefaultAsync(
            user => user.NormalizedUserName == normalizedUserName,
            stoppingToken
        );

    /// <inheritdoc />
    public Task<UserEntity> FindByNormalizedEmailAsync(
        string normalizedEmail,
        CancellationToken stoppingToken = default
    ) => Set.FirstOrDefaultAsync(user => user.NormalizedEmail == normalizedEmail, stoppingToken);

    /// <inheritdoc />
    public Task<UserEntity> FindByExternalSubjectAsync(
        string provider,
        string subjectId,
        CancellationToken stoppingToken = default
    ) =>
        Set.FirstOrDefaultAsync(
            user =>
                user.ExternalIdentityProvider == provider && user.ExternalSubjectId == subjectId,
            stoppingToken
        );

    /// <inheritdoc />
    public async Task<IReadOnlyList<RoleEntity>> ListRolesAsync(
        Guid userId,
        CancellationToken stoppingToken = default
    ) =>
        await (
            from userRole in Context.UserRoles
            join role in Context.Roles on userRole.RoleId equals role.Id
            where userRole.UserId == userId
            select role
        ).ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserClaimEntity>> ListClaimsAsync(
        Guid userId,
        CancellationToken stoppingToken = default
    ) => await Context.UserClaims.Where(claim => claim.UserId == userId).ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task AddToRoleAsync(
        Guid userId,
        Guid roleId,
        CancellationToken stoppingToken = default
    ) =>
        await Context.UserRoles.AddAsync(
            new UserRoleEntity { UserId = userId, RoleId = roleId },
            stoppingToken
        );

    /// <inheritdoc />
    public async Task RemoveFromRoleAsync(
        Guid userId,
        Guid roleId,
        CancellationToken stoppingToken = default
    )
    {
        // Look up the existing membership row.
        UserRoleEntity membership = await Context.UserRoles.FirstOrDefaultAsync(
            userRole => userRole.UserId == userId && userRole.RoleId == roleId,
            stoppingToken
        );

        // There is nothing to remove if the user never held the role in the first place.
        if (membership is null)
            return;

        // We're done, remove the membership row.
        Context.UserRoles.Remove(membership);
    }

    /// <inheritdoc />
    public async Task AddClaimsAsync(
        Guid userId,
        IEnumerable<(string ClaimType, string ClaimValue)> claims,
        CancellationToken stoppingToken = default
    ) =>
        await Context.UserClaims.AddRangeAsync(
            claims.Select(claim => new UserClaimEntity
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ClaimType = claim.ClaimType,
                ClaimValue = claim.ClaimValue,
            }),
            stoppingToken
        );

    /// <inheritdoc />
    public async Task RemoveClaimsAsync(
        Guid userId,
        IEnumerable<(string ClaimType, string ClaimValue)> claims,
        CancellationToken stoppingToken = default
    )
    {
        // Localize the requested claims so the query below isn't re-evaluated per row.
        (string ClaimType, string ClaimValue)[] requested = [.. claims];

        // Look up every one of the user's claims that matches one of the requested pairs.
        List<UserClaimEntity> matches = await Context
            .UserClaims.Where(claim => claim.UserId == userId)
            .ToListAsync(stoppingToken);

        // We're done, remove every match.
        Context.UserClaims.RemoveRange(
            matches.Where(claim =>
                requested.Any(r =>
                    r.ClaimType == claim.ClaimType && r.ClaimValue == claim.ClaimValue
                )
            )
        );
    }

    /// <inheritdoc />
    public async Task ReplaceClaimAsync(
        Guid userId,
        string currentClaimType,
        string currentClaimValue,
        string newClaimType,
        string newClaimValue,
        CancellationToken stoppingToken = default
    )
    {
        // Look up every one of the user's claims that matches the claim being replaced.
        List<UserClaimEntity> matches = await Context
            .UserClaims.Where(claim =>
                claim.UserId == userId
                && claim.ClaimType == currentClaimType
                && claim.ClaimValue == currentClaimValue
            )
            .ToListAsync(stoppingToken);

        // We're done, overwrite each match with the replacement claim.
        foreach (UserClaimEntity claim in matches)
        {
            claim.ClaimType = newClaimType;
            claim.ClaimValue = newClaimValue;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserEntity>> ListByClaimAsync(
        string claimType,
        string claimValue,
        CancellationToken stoppingToken = default
    ) =>
        await (
            from claim in Context.UserClaims
            join user in Set on claim.UserId equals user.Id
            where claim.ClaimType == claimType && claim.ClaimValue == claimValue
            select user
        ).ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) => await Set.OrderBy(user => user.UserName).Skip(skip).Take(take).ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken stoppingToken = default) =>
        await Set.CountAsync(stoppingToken);
}
