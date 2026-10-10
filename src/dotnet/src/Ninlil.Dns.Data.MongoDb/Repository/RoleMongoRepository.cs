using MongoDB.Driver;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Repository;

/// <summary>
///     This class implements <see cref="IRoleRepository" /> against MongoDB.
/// </summary>
/// <param name="context">The context to read collections from.</param>
/// <param name="changeTracker">The change tracker to stage writes onto.</param>
public class RoleMongoRepository(NinlilDnsMongoContext context, MongoChangeTracker changeTracker)
    : MongoRepositoryBase<RoleEntity>(context.Roles, changeTracker),
        IRoleRepository
{
    /// <inheritdoc />
    public Task<RoleEntity> FindByNormalizedNameAsync(
        string normalizedName,
        CancellationToken stoppingToken = default
    ) =>
        Collection
            .Find(role => role.NormalizedName == normalizedName)
            .FirstOrDefaultAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<RoleClaimEntity>> ListClaimsAsync(
        Guid roleId,
        CancellationToken stoppingToken = default
    ) => await context.RoleClaims.Find(claim => claim.RoleId == roleId).ToListAsync(stoppingToken);

    /// <inheritdoc />
    public Task AddClaimAsync(
        Guid roleId,
        string claimType,
        string claimValue,
        CancellationToken stoppingToken = default
    )
    {
        ChangeTracker.Stage(
            typeof(RoleClaimEntity),
            (session, token) =>
                context.RoleClaims.InsertOneWithSessionAsync(
                    session,
                    new RoleClaimEntity
                    {
                        Id = Guid.NewGuid(),
                        RoleId = roleId,
                        ClaimType = claimType,
                        ClaimValue = claimValue,
                    },
                    token
                )
        );

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveClaimAsync(
        Guid roleId,
        string claimType,
        string claimValue,
        CancellationToken stoppingToken = default
    )
    {
        // The same claim can have been granted more than once, so remove every match.
        ChangeTracker.Stage(
            typeof(RoleClaimEntity),
            (session, token) =>
                context.RoleClaims.DeleteManyWithSessionAsync(
                    session,
                    claim =>
                        claim.RoleId == roleId
                        && claim.ClaimType == claimType
                        && claim.ClaimValue == claimValue,
                    token
                )
        );

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserEntity>> ListUsersInRoleAsync(
        string normalizedRoleName,
        CancellationToken stoppingToken = default
    )
    {
        // No joins in a find: load the role, then its memberships, then the users.
        RoleEntity role = await Collection
            .Find(candidate => candidate.NormalizedName == normalizedRoleName)
            .FirstOrDefaultAsync(stoppingToken);

        if (role is null)
            return [];

        List<UserRoleEntity> memberships = await context
            .UserRoles.Find(userRole => userRole.RoleId == role.Id)
            .ToListAsync(stoppingToken);

        if (memberships.Count == 0)
            return [];

        HashSet<Guid> userIds = [.. memberships.Select(membership => membership.UserId)];
        return await context
            .Users.Find(user => userIds.Contains(user.Id))
            .ToListAsync(stoppingToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RoleEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Collection
            .Find(FilterDefinition<RoleEntity>.Empty)
            .SortBy(role => role.Name)
            .Skip(skip)
            .Limit(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken stoppingToken = default) =>
        (int)
            await Collection.CountDocumentsAsync(
                FilterDefinition<RoleEntity>.Empty,
                cancellationToken: stoppingToken
            );
}
