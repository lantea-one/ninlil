using MongoDB.Driver;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Repository;

/// <summary>
///     This class implements <see cref="IClusterNodeRepository" /> against MongoDB.
/// </summary>
/// <param name="context">The context to read collections from.</param>
/// <param name="changeTracker">The change tracker to stage writes onto.</param>
public class ClusterNodeMongoRepository(
    NinlilDnsMongoContext context,
    MongoChangeTracker changeTracker
)
    : MongoRepositoryBase<ClusterNodeEntity>(context.ClusterNodes, changeTracker),
        IClusterNodeRepository
{
    /// <inheritdoc />
    public Task<ClusterNodeEntity> FindByRemoteNodeIdAsync(
        Guid remoteNodeId,
        CancellationToken stoppingToken = default
    ) =>
        Collection
            .Find(node => node.RemoteNodeId == remoteNodeId)
            .FirstOrDefaultAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ClusterNodeEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Collection
            .Find(FilterDefinition<ClusterNodeEntity>.Empty)
            .SortBy(node => node.Name)
            .Skip(skip)
            .Limit(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken stoppingToken = default) =>
        (int)
            await Collection.CountDocumentsAsync(
                FilterDefinition<ClusterNodeEntity>.Empty,
                cancellationToken: stoppingToken
            );
}
