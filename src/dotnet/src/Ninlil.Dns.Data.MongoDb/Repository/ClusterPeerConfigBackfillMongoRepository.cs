using MongoDB.Driver;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Repository;

/// <summary>
///     This class implements <see cref="IClusterPeerConfigBackfillRepository" /> against MongoDB.
/// </summary>
/// <param name="context">The context to read collections from.</param>
/// <param name="changeTracker">The change tracker to stage writes onto.</param>
public class ClusterPeerConfigBackfillMongoRepository(
    NinlilDnsMongoContext context,
    MongoChangeTracker changeTracker
)
    : MongoRepositoryBase<ClusterPeerConfigBackfillEntity>(
        context.ClusterPeerConfigBackfills,
        changeTracker
    ),
        IClusterPeerConfigBackfillRepository
{
    /// <inheritdoc />
    public Task<ClusterPeerConfigBackfillEntity> FindByClusterNodeIdAsync(
        Guid clusterNodeId,
        CancellationToken stoppingToken = default
    ) =>
        Collection
            .Find(request => request.ClusterNodeId == clusterNodeId)
            .FirstOrDefaultAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> ListPendingClusterNodeIdsAsync(
        CancellationToken stoppingToken = default
    ) =>
        await Collection
            .Find(FilterDefinition<ClusterPeerConfigBackfillEntity>.Empty)
            .Project(request => request.ClusterNodeId)
            .ToListAsync(stoppingToken);
}
