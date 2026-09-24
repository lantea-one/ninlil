using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Repository;

/// <summary>
///     This class implements <see cref="IClusterNodeRepository" /> against
///     <see cref="NinlilDnsContext" />.
/// </summary>
/// <param name="context">The database context to operate against.</param>
public class ClusterNodeRepository(NinlilDnsContext context)
    : EntityFrameworkRepositoryBase<ClusterNodeEntity>(context),
        IClusterNodeRepository
{
    /// <inheritdoc />
    public Task<ClusterNodeEntity> FindByRemoteNodeIdAsync(
        Guid remoteNodeId,
        CancellationToken stoppingToken = default
    ) => Set.FirstOrDefaultAsync(node => node.RemoteNodeId == remoteNodeId, stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ClusterNodeEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) => await Set.OrderBy(node => node.Name).Skip(skip).Take(take).ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken stoppingToken = default) =>
        await Set.CountAsync(stoppingToken);
}
