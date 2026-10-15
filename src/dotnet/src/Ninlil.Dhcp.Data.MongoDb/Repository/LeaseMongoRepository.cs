using MongoDB.Driver;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.MongoDb;
using Ninlil.Dns.Data.MongoDb.Repository;

// Define our namespace.
namespace Ninlil.Dhcp.Data.MongoDb.Repository;

/// <summary>
///     This class implements <see cref="ILeaseRepository" /> against MongoDB.
/// </summary>
/// <param name="context">The context to read collections from.</param>
/// <param name="changeTracker">The change tracker to stage writes onto.</param>
public class LeaseMongoRepository(NinlilDnsMongoContext context, MongoChangeTracker changeTracker)
    : MongoRepositoryBase<LeaseEntity>(context.Leases, changeTracker),
        ILeaseRepository
{
    /// <inheritdoc />
    public Task<LeaseEntity> FindByClientIdentifierAsync(
        string scopeName,
        string clientIdentifier,
        CancellationToken stoppingToken = default
    ) =>
        Collection
            .Find(lease =>
                lease.ScopeName == scopeName && lease.ClientIdentifier == clientIdentifier
            )
            .FirstOrDefaultAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<LeaseEntity>> ListByScopeNameAsync(
        string scopeName,
        CancellationToken stoppingToken = default
    ) => await Collection.Find(lease => lease.ScopeName == scopeName).ToListAsync(stoppingToken);

    /// <inheritdoc />
    public Task<LeaseEntity> FindByIpAddressAsync(
        string scopeName,
        string ipAddress,
        CancellationToken stoppingToken = default
    ) =>
        Collection
            .Find(lease => lease.ScopeName == scopeName && lease.IpAddress == ipAddress)
            .FirstOrDefaultAsync(stoppingToken);

    /// <inheritdoc />
    public Task<LeaseEntity> FindActiveByIpAddressAsync(
        string ipAddress,
        CancellationToken stoppingToken = default
    ) =>
        Collection
            .Find(lease => lease.IpAddress == ipAddress && lease.State == LeaseStateEnum.Active)
            .FirstOrDefaultAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<LeaseEntity>> ListActiveAsync(
        DateTimeOffset nowUtc,
        CancellationToken stoppingToken = default
    ) =>
        await Collection
            .Find(lease =>
                lease.State == LeaseStateEnum.Active
                && (lease.LeaseExpiresAtUtc == null || lease.LeaseExpiresAtUtc > nowUtc)
            )
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<LeaseEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Collection
            .Find(FilterDefinition<LeaseEntity>.Empty)
            .SortBy(lease => lease.ScopeName)
            .ThenBy(lease => lease.ClientIdentifier)
            .Skip(skip)
            .Limit(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken stoppingToken = default) =>
        (int)
            await Collection.CountDocumentsAsync(
                FilterDefinition<LeaseEntity>.Empty,
                cancellationToken: stoppingToken
            );

    /// <inheritdoc />
    public async Task<IReadOnlyList<LeaseEntity>> SearchByScopeNameAsync(
        string scopeName,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Collection
            .Find(lease => lease.ScopeName == scopeName)
            .SortBy(lease => lease.ClientIdentifier)
            .Skip(skip)
            .Limit(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountByScopeNameAsync(
        string scopeName,
        CancellationToken stoppingToken = default
    ) =>
        (int)
            await Collection.CountDocumentsAsync(
                lease => lease.ScopeName == scopeName,
                cancellationToken: stoppingToken
            );

    /// <inheritdoc />
    public async Task<bool> TryExpireAsync(
        Guid leaseId,
        DateTimeOffset nowUtc,
        CancellationToken stoppingToken = default
    )
    {
        // One atomic update-one with the condition in its filter, not staged in the change
        // tracker, so a renewal that committed first is never overwritten.
        UpdateResult result = await Collection.UpdateOneAsync(
            lease =>
                lease.Id == leaseId
                && lease.State == LeaseStateEnum.Active
                && lease.LeaseExpiresAtUtc != null
                && lease.LeaseExpiresAtUtc <= nowUtc,
            Builders<LeaseEntity>
                .Update.Set(lease => lease.State, LeaseStateEnum.Expired)
                .Set(lease => lease.UpdatedAtUtc, nowUtc),
            cancellationToken: stoppingToken
        );

        return result.ModifiedCount == 1;
    }
}
