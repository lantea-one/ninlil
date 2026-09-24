using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IRoleRepository" /> against
///     <see cref="NinlilDnsContext" />.
/// </summary>
/// <param name="context">The database context to operate against.</param>
public class RoleRepository(NinlilDnsContext context)
    : EntityFrameworkRepositoryBase<RoleEntity>(context),
        IRoleRepository
{
    /// <inheritdoc />
    public Task<RoleEntity> FindByNormalizedNameAsync(
        string normalizedName,
        CancellationToken stoppingToken = default
    ) => Set.FirstOrDefaultAsync(role => role.NormalizedName == normalizedName, stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<RoleClaimEntity>> ListClaimsAsync(
        Guid roleId,
        CancellationToken stoppingToken = default
    ) => await Context.RoleClaims.Where(claim => claim.RoleId == roleId).ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task AddClaimAsync(
        Guid roleId,
        string claimType,
        string claimValue,
        CancellationToken stoppingToken = default
    ) =>
        await Context.RoleClaims.AddAsync(
            new RoleClaimEntity
            {
                Id = Guid.NewGuid(),
                RoleId = roleId,
                ClaimType = claimType,
                ClaimValue = claimValue,
            },
            stoppingToken
        );

    /// <inheritdoc />
    public async Task RemoveClaimAsync(
        Guid roleId,
        string claimType,
        string claimValue,
        CancellationToken stoppingToken = default
    )
    {
        // Look up every one of the role's claims that matches the requested pair. Nothing stops
        // the exact same claim from having been granted more than once, so every match is
        // removed rather than assuming there is exactly one.
        List<RoleClaimEntity> matches = await Context
            .RoleClaims.Where(claim =>
                claim.RoleId == roleId
                && claim.ClaimType == claimType
                && claim.ClaimValue == claimValue
            )
            .ToListAsync(stoppingToken);

        // We're done, remove every match.
        Context.RoleClaims.RemoveRange(matches);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserEntity>> ListUsersInRoleAsync(
        string normalizedRoleName,
        CancellationToken stoppingToken = default
    ) =>
        await (
            from role in Set
            where role.NormalizedName == normalizedRoleName
            join userRole in Context.UserRoles on role.Id equals userRole.RoleId
            join user in Context.Users on userRole.UserId equals user.Id
            select user
        ).ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<RoleEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) => await Set.OrderBy(role => role.Name).Skip(skip).Take(take).ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken stoppingToken = default) =>
        await Set.CountAsync(stoppingToken);
}
