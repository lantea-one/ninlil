using MongoDB.Driver;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Repository;

/// <summary>
///     This class implements <see cref="IClusterSyncTombstoneRepository" /> against MongoDB.
/// </summary>
/// <param name="context">The context to read collections from.</param>
/// <param name="changeTracker">The change tracker to stage writes onto.</param>
public class ClusterSyncTombstoneMongoRepository(
    NinlilDnsMongoContext context,
    MongoChangeTracker changeTracker
)
    : MongoRepositoryBase<ClusterSyncTombstoneEntity>(context.ClusterSyncTombstones, changeTracker),
        IClusterSyncTombstoneRepository
{
    /// <inheritdoc />
    public Task<ClusterSyncTombstoneEntity> FindAsync(
        string entityType,
        string entityKey,
        CancellationToken stoppingToken = default
    ) =>
        Collection
            .Find(tombstone =>
                tombstone.EntityType == entityType && tombstone.EntityKey == entityKey
            )
            .FirstOrDefaultAsync(stoppingToken);
}
