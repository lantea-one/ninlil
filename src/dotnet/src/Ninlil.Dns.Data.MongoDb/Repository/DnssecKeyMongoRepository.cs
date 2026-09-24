using MongoDB.Driver;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Repository;

/// <summary>
///     This class implements <see cref="IDnssecKeyRepository" /> against MongoDB.
/// </summary>
/// <param name="context">The context to read collections from.</param>
/// <param name="changeTracker">The change tracker to stage writes onto.</param>
public class DnssecKeyMongoRepository(
    NinlilDnsMongoContext context,
    MongoChangeTracker changeTracker
) : MongoRepositoryBase<DnssecKeyEntity>(context.DnssecKeys, changeTracker), IDnssecKeyRepository
{
    /// <inheritdoc />
    public Task<IReadOnlyList<DnssecKeyEntity>> ListByZoneIdAsync(
        Guid zoneId,
        CancellationToken stoppingToken = default
    ) => ListAsync(key => key.ZoneId == zoneId, stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DnssecKeyEntity>> SearchByZoneIdAsync(
        Guid zoneId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    )
    {
        SortDefinition<DnssecKeyEntity> roleThenCreatedAt = Builders<DnssecKeyEntity>.Sort.Combine(
            Builders<DnssecKeyEntity>.Sort.Ascending(key => key.Role),
            Builders<DnssecKeyEntity>.Sort.Ascending(key => key.CreatedAtUtc)
        );

        return await Collection
            .Find(key => key.ZoneId == zoneId)
            .Sort(roleThenCreatedAt)
            .Skip(skip)
            .Limit(take)
            .ToListAsync(stoppingToken);
    }

    /// <inheritdoc />
    public async Task<int> CountByZoneIdAsync(
        Guid zoneId,
        CancellationToken stoppingToken = default
    ) =>
        (int)
            await Collection.CountDocumentsAsync(
                key => key.ZoneId == zoneId,
                cancellationToken: stoppingToken
            );
}
