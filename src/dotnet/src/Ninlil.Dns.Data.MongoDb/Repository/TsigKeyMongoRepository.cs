using MongoDB.Driver;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Repository;

/// <summary>
///     This class implements <see cref="ITsigKeyRepository" /> against MongoDB.
/// </summary>
/// <param name="context">The context to read collections from.</param>
/// <param name="changeTracker">The change tracker to stage writes onto.</param>
public class TsigKeyMongoRepository(NinlilDnsMongoContext context, MongoChangeTracker changeTracker)
    : MongoRepositoryBase<TsigKeyEntity>(context.TsigKeys, changeTracker),
        ITsigKeyRepository
{
    /// <inheritdoc />
    public Task<TsigKeyEntity> FindByNameAsync(
        string name,
        CancellationToken stoppingToken = default
    ) => Collection.Find(key => key.Name == name).FirstOrDefaultAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<TsigKeyEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Collection
            .Find(FilterDefinition<TsigKeyEntity>.Empty)
            .SortBy(key => key.Name)
            .Skip(skip)
            .Limit(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken stoppingToken = default) =>
        (int)
            await Collection.CountDocumentsAsync(
                FilterDefinition<TsigKeyEntity>.Empty,
                cancellationToken: stoppingToken
            );
}
