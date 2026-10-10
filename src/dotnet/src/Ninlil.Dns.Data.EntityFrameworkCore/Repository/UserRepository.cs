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
        UserRoleEntity membership = await Context.UserRoles.FirstOrDefaultAsync(
            userRole => userRole.UserId == userId && userRole.RoleId == roleId,
            stoppingToken
        );

        if (membership is null)
            return;

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
        // Materialize the requested pairs once so the filter below does not re-enumerate them.
        (string ClaimType, string ClaimValue)[] requested = [.. claims];

        // Load the user's claims and match the pairs in memory, since a tuple list does not
        // translate to SQL.
        List<UserClaimEntity> matches = await Context
            .UserClaims.Where(claim => claim.UserId == userId)
            .ToListAsync(stoppingToken);

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
        List<UserClaimEntity> matches = await Context
            .UserClaims.Where(claim =>
                claim.UserId == userId
                && claim.ClaimType == currentClaimType
                && claim.ClaimValue == currentClaimValue
            )
            .ToListAsync(stoppingToken);

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
    ) =>
        await Set.AsNoTracking()
            .OrderBy(user => user.UserName)
            .Skip(skip)
            .Take(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken stoppingToken = default) =>
        await Set.CountAsync(stoppingToken);
}
