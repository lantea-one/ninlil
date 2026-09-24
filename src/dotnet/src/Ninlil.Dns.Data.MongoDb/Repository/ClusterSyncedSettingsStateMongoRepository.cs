using MongoDB.Driver;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Repository;

/// <summary>
///     This class implements <see cref="IClusterSyncedSettingsStateRepository" /> against
///     MongoDB.
/// </summary>
/// <param name="context">The context to read collections from.</param>
/// <param name="changeTracker">The change tracker to stage writes onto.</param>
public class ClusterSyncedSettingsStateMongoRepository(
    NinlilDnsMongoContext context,
    MongoChangeTracker changeTracker
)
    : MongoRepositoryBase<ClusterSyncedSettingsStateEntity>(
        context.ClusterSyncedSettingsStates,
        changeTracker
    ),
        IClusterSyncedSettingsStateRepository
{
    /// <inheritdoc />
    public Task<ClusterSyncedSettingsStateEntity> FindBySectionNameAsync(
        string sectionName,
        CancellationToken stoppingToken = default
    ) =>
        Collection
            .Find(state => state.SectionName == sectionName)
            .FirstOrDefaultAsync(stoppingToken);
}
