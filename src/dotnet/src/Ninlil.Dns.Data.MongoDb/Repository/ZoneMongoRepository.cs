using MongoDB.Driver;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Repository;

/// <summary>
///     This class implements <see cref="IZoneRepository" /> against MongoDB.
/// </summary>
/// <param name="context">The context to read collections from.</param>
/// <param name="changeTracker">The change tracker to stage writes onto.</param>
public class ZoneMongoRepository(NinlilDnsMongoContext context, MongoChangeTracker changeTracker)
    : MongoRepositoryBase<ZoneEntity>(context.Zones, changeTracker),
        IZoneRepository
{
    /// <inheritdoc />
    public Task<ZoneEntity> FindByNameAsync(
        string name,
        CancellationToken stoppingToken = default
    ) => Collection.Find(zone => zone.Name == name).FirstOrDefaultAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ZoneEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Collection
            .Find(FilterDefinition<ZoneEntity>.Empty)
            .SortBy(zone => zone.Name)
            .Skip(skip)
            .Limit(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken stoppingToken = default) =>
        (int)
            await Collection.CountDocumentsAsync(
                FilterDefinition<ZoneEntity>.Empty,
                cancellationToken: stoppingToken
            );
}
