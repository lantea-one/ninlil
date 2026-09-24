using MongoDB.Driver;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Repository;

/// <summary>
///     This class implements <see cref="IUserRepository" /> against MongoDB.
/// </summary>
/// <param name="context">The context to read collections from.</param>
/// <param name="changeTracker">The change tracker to stage writes onto.</param>
public class UserMongoRepository(NinlilDnsMongoContext context, MongoChangeTracker changeTracker)
    : MongoRepositoryBase<UserEntity>(context.Users, changeTracker),
        IUserRepository
{
    /// <inheritdoc />
    public Task<UserEntity> FindByNormalizedUserNameAsync(
        string normalizedUserName,
        CancellationToken stoppingToken = default
    ) =>
        Collection
            .Find(user => user.NormalizedUserName == normalizedUserName)
            .FirstOrDefaultAsync(stoppingToken);

    /// <inheritdoc />
    public Task<UserEntity> FindByNormalizedEmailAsync(
        string normalizedEmail,
        CancellationToken stoppingToken = default
    ) =>
        Collection
            .Find(user => user.NormalizedEmail == normalizedEmail)
            .FirstOrDefaultAsync(stoppingToken);

    /// <inheritdoc />
    public Task<UserEntity> FindByExternalSubjectAsync(
        string provider,
        string subjectId,
        CancellationToken stoppingToken = default
    ) =>
        Collection
            .Find(user =>
                user.ExternalIdentityProvider == provider && user.ExternalSubjectId == subjectId
            )
            .FirstOrDefaultAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<RoleEntity>> ListRolesAsync(
        Guid userId,
        CancellationToken stoppingToken = default
    )
    {
        // Look up which role identifiers this user holds.
        List<UserRoleEntity> memberships = await context
            .UserRoles.Find(userRole => userRole.UserId == userId)
            .ToListAsync(stoppingToken);

        // There is nothing more to look up if the user holds no roles at all.
        if (memberships.Count == 0)
            return [];

        // We're done, look up and return the actual role documents.
        HashSet<Guid> roleIds = [.. memberships.Select(membership => membership.RoleId)];
        return await context
            .Roles.Find(role => roleIds.Contains(role.Id))
            .ToListAsync(stoppingToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserClaimEntity>> ListClaimsAsync(
        Guid userId,
        CancellationToken stoppingToken = default
    ) => await context.UserClaims.Find(claim => claim.UserId == userId).ToListAsync(stoppingToken);

    /// <inheritdoc />
    public Task AddToRoleAsync(Guid userId, Guid roleId, CancellationToken stoppingToken = default)
    {
        // Stage the membership insert rather than performing it immediately.
        ChangeTracker.Stage(token =>
            context.UserRoles.InsertOneAsync(
                new UserRoleEntity { UserId = userId, RoleId = roleId },
                cancellationToken: token
            )
        );

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveFromRoleAsync(
        Guid userId,
        Guid roleId,
        CancellationToken stoppingToken = default
    )
    {
        // Stage the membership removal rather than performing it immediately.
        ChangeTracker.Stage(token =>
            context.UserRoles.DeleteOneAsync(
                userRole => userRole.UserId == userId && userRole.RoleId == roleId,
                token
            )
        );

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task AddClaimsAsync(
        Guid userId,
        IEnumerable<(string ClaimType, string ClaimValue)> claims,
        CancellationToken stoppingToken = default
    )
    {
        // Stage the inserts rather than performing them immediately.
        ChangeTracker.Stage(token =>
            context.UserClaims.InsertManyAsync(
                claims.Select(claim => new UserClaimEntity
                {
                    Id = Guid.NewGuid(),
                    UserId = userId,
                    ClaimType = claim.ClaimType,
                    ClaimValue = claim.ClaimValue,
                }),
                cancellationToken: token
            )
        );

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveClaimsAsync(
        Guid userId,
        IEnumerable<(string ClaimType, string ClaimValue)> claims,
        CancellationToken stoppingToken = default
    )
    {
        // Localize the requested claims so the staged delegate doesn't capture a lazily
        // re-evaluated enumerable.
        (string ClaimType, string ClaimValue)[] requested = [.. claims];

        // Stage the removal of every one of the user's claims that matches one of the requested
        // pairs, rather than performing it immediately.
        ChangeTracker.Stage(token =>
            context.UserClaims.DeleteManyAsync(
                claim =>
                    claim.UserId == userId
                    && requested.Any(r =>
                        r.ClaimType == claim.ClaimType && r.ClaimValue == claim.ClaimValue
                    ),
                token
            )
        );

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ReplaceClaimAsync(
        Guid userId,
        string currentClaimType,
        string currentClaimValue,
        string newClaimType,
        string newClaimValue,
        CancellationToken stoppingToken = default
    )
    {
        // Stage the update rather than performing it immediately.
        ChangeTracker.Stage(token =>
            context.UserClaims.UpdateManyAsync(
                claim =>
                    claim.UserId == userId
                    && claim.ClaimType == currentClaimType
                    && claim.ClaimValue == currentClaimValue,
                Builders<UserClaimEntity>
                    .Update.Set(claim => claim.ClaimType, newClaimType)
                    .Set(claim => claim.ClaimValue, newClaimValue),
                cancellationToken: token
            )
        );

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserEntity>> ListByClaimAsync(
        string claimType,
        string claimValue,
        CancellationToken stoppingToken = default
    )
    {
        // Look up which user identifiers carry a matching claim.
        List<UserClaimEntity> matches = await context
            .UserClaims.Find(claim =>
                claim.ClaimType == claimType && claim.ClaimValue == claimValue
            )
            .ToListAsync(stoppingToken);

        // There is nothing more to look up if nobody carries the claim.
        if (matches.Count == 0)
            return [];

        // We're done, look up and return the actual user documents.
        HashSet<Guid> userIds = [.. matches.Select(claim => claim.UserId)];
        return await Collection.Find(user => userIds.Contains(user.Id)).ToListAsync(stoppingToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Collection
            .Find(FilterDefinition<UserEntity>.Empty)
            .SortBy(user => user.UserName)
            .Skip(skip)
            .Limit(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken stoppingToken = default) =>
        (int)
            await Collection.CountDocumentsAsync(
                FilterDefinition<UserEntity>.Empty,
                cancellationToken: stoppingToken
            );
}
