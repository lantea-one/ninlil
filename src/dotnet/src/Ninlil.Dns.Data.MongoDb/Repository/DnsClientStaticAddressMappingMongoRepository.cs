using MongoDB.Driver;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Repository;

/// <summary>
///     This class implements <see cref="IDnsClientStaticAddressMappingRepository" /> against
///     MongoDB.
/// </summary>
/// <param name="context">The context to read collections from.</param>
/// <param name="changeTracker">The change tracker to stage writes onto.</param>
public class DnsClientStaticAddressMappingMongoRepository(
    NinlilDnsMongoContext context,
    MongoChangeTracker changeTracker
)
    : MongoRepositoryBase<DnsClientStaticAddressMappingEntity>(
        context.DnsClientStaticAddressMappings,
        changeTracker
    ),
        IDnsClientStaticAddressMappingRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsClientStaticAddressMappingEntity>> ListEnabledAsync(
        CancellationToken stoppingToken = default
    ) => await Collection.Find(mapping => !mapping.IsDisabled).ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsClientStaticAddressMappingEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Collection
            .Find(FilterDefinition<DnsClientStaticAddressMappingEntity>.Empty)
            .SortBy(mapping => mapping.Cidr)
            .Skip(skip)
            .Limit(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken stoppingToken = default) =>
        (int)
            await Collection.CountDocumentsAsync(
                FilterDefinition<DnsClientStaticAddressMappingEntity>.Empty,
                cancellationToken: stoppingToken
            );
}
