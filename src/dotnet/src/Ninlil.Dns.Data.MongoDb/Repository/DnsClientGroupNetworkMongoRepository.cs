using MongoDB.Driver;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Repository;

/// <summary>
///     This class implements <see cref="IDnsClientGroupNetworkRepository" /> against MongoDB.
/// </summary>
/// <param name="context">The context to read collections from.</param>
/// <param name="changeTracker">The change tracker to stage writes onto.</param>
public class DnsClientGroupNetworkMongoRepository(
    NinlilDnsMongoContext context,
    MongoChangeTracker changeTracker
)
    : MongoRepositoryBase<DnsClientGroupNetworkEntity>(
        context.DnsClientGroupNetworks,
        changeTracker
    ),
        IDnsClientGroupNetworkRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsClientGroupNetworkEntity>> ListByGroupIdAsync(
        Guid clientGroupId,
        CancellationToken stoppingToken = default
    ) =>
        await Collection
            .Find(network => network.ClientGroupId == clientGroupId)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsClientGroupNetworkEntity>> SearchByGroupIdAsync(
        Guid clientGroupId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Collection
            .Find(network => network.ClientGroupId == clientGroupId)
            .SortBy(network => network.Cidr)
            .Skip(skip)
            .Limit(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountByGroupIdAsync(
        Guid clientGroupId,
        CancellationToken stoppingToken = default
    ) =>
        (int)
            await Collection.CountDocumentsAsync(
                network => network.ClientGroupId == clientGroupId,
                cancellationToken: stoppingToken
            );
}
