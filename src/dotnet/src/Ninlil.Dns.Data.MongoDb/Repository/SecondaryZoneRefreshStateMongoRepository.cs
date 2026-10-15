using MongoDB.Driver;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Repository;

/// <summary>
///     This class implements <see cref="ISecondaryZoneRefreshStateRepository" /> against MongoDB.
/// </summary>
/// <param name="context">The context to read collections from.</param>
/// <param name="changeTracker">The change tracker to stage writes onto.</param>
public class SecondaryZoneRefreshStateMongoRepository(
    NinlilDnsMongoContext context,
    MongoChangeTracker changeTracker
)
    : MongoRepositoryBase<SecondaryZoneRefreshStateEntity>(
        context.SecondaryZoneRefreshStates,
        changeTracker
    ),
        ISecondaryZoneRefreshStateRepository
{
    /// <inheritdoc />
    public async Task<SecondaryZoneRefreshStateEntity> FindByZoneIdAsync(
        Guid zoneId,
        CancellationToken stoppingToken = default
    ) => await Collection.Find(state => state.ZoneId == zoneId).FirstOrDefaultAsync(stoppingToken);
}
