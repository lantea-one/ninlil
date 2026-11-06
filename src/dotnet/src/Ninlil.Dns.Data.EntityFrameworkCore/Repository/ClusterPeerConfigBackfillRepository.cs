using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IClusterPeerConfigBackfillRepository" /> against
///     <see cref="NinlilDnsContext" />.
/// </summary>
/// <param name="context">The database context to operate against.</param>
public class ClusterPeerConfigBackfillRepository(NinlilDnsContext context)
    : EntityFrameworkRepositoryBase<ClusterPeerConfigBackfillEntity>(context),
        IClusterPeerConfigBackfillRepository
{
    /// <inheritdoc />
    public Task<ClusterPeerConfigBackfillEntity> FindByClusterNodeIdAsync(
        Guid clusterNodeId,
        CancellationToken stoppingToken = default
    ) => Set.FirstOrDefaultAsync(request => request.ClusterNodeId == clusterNodeId, stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> ListPendingClusterNodeIdsAsync(
        CancellationToken stoppingToken = default
    ) =>
        await Set.AsNoTracking()
            .Select(request => request.ClusterNodeId)
            .ToListAsync(stoppingToken);
}
