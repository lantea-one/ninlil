using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookup cluster config
///     sync needs before it creates or deletes a synced row: the tombstone, if any, of one entity.
/// </summary>
public interface IClusterSyncTombstoneRepository : IRepository<ClusterSyncTombstoneEntity>
{
    /// <summary>
    ///     This method looks up the tombstone of one synced entity.
    /// </summary>
    /// <param name="entityType">The synced entity type, for example <c>"User"</c>.</param>
    /// <param name="entityKey">The entity's key.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The tombstone, or <see langword="null" /> if the entity was never deleted.</returns>
    Task<ClusterSyncTombstoneEntity> FindAsync(
        string entityType,
        string entityKey,
        CancellationToken stoppingToken = default
    );
}
