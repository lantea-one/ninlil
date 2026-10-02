using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IClusterSyncTombstoneRepository" /> against
///     <see cref="NinlilDnsContext" />.
/// </summary>
/// <param name="context">The database context to operate against.</param>
public class ClusterSyncTombstoneRepository(NinlilDnsContext context)
    : EntityFrameworkRepositoryBase<ClusterSyncTombstoneEntity>(context),
        IClusterSyncTombstoneRepository
{
    /// <inheritdoc />
    public Task<ClusterSyncTombstoneEntity> FindAsync(
        string entityType,
        string entityKey,
        CancellationToken stoppingToken = default
    ) =>
        Set.FirstOrDefaultAsync(
            tombstone => tombstone.EntityType == entityType && tombstone.EntityKey == entityKey,
            stoppingToken
        );
}
