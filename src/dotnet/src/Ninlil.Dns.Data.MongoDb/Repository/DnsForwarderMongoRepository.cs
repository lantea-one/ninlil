using MongoDB.Driver;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Repository;

/// <summary>
///     This class implements <see cref="IDnsForwarderRepository" /> against MongoDB.
/// </summary>
/// <param name="context">The context to read collections from.</param>
/// <param name="changeTracker">The change tracker to stage writes onto.</param>
public class DnsForwarderMongoRepository(
    NinlilDnsMongoContext context,
    MongoChangeTracker changeTracker
)
    : MongoRepositoryBase<DnsForwarderEntity>(context.DnsForwarders, changeTracker),
        IDnsForwarderRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsForwarderEntity>> ListEnabledOrderedAsync(
        CancellationToken stoppingToken = default
    ) =>
        await Collection
            .Find(forwarder => !forwarder.IsDisabled)
            .SortBy(forwarder => forwarder.SortOrder)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsForwarderEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    )
    {
        SortDefinition<DnsForwarderEntity> sortOrderThenName =
            Builders<DnsForwarderEntity>.Sort.Combine(
                Builders<DnsForwarderEntity>.Sort.Ascending(forwarder => forwarder.SortOrder),
                Builders<DnsForwarderEntity>.Sort.Ascending(forwarder => forwarder.Name)
            );

        return await Collection
            .Find(FilterDefinition<DnsForwarderEntity>.Empty)
            .Sort(sortOrderThenName)
            .Skip(skip)
            .Limit(take)
            .ToListAsync(stoppingToken);
    }

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken stoppingToken = default) =>
        (int)
            await Collection.CountDocumentsAsync(
                FilterDefinition<DnsForwarderEntity>.Empty,
                cancellationToken: stoppingToken
            );
}
