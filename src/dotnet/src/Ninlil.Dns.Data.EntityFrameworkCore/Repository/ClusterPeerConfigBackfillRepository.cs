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

    /// <inheritdoc />
    public async Task<bool> RemoveIfUnchangedAsync(
        Guid clusterNodeId,
        DateTimeOffset requestedAtUtc,
        CancellationToken stoppingToken = default
    ) =>
        await Set.Where(request =>
                request.ClusterNodeId == clusterNodeId && request.RequestedAtUtc == requestedAtUtc
            )
            .ExecuteDeleteAsync(stoppingToken) > 0;

    /// <inheritdoc />
    public async Task<bool> TouchAsync(
        Guid clusterNodeId,
        DateTimeOffset requestedAtUtc,
        CancellationToken stoppingToken = default
    ) =>
        await Set.Where(request => request.ClusterNodeId == clusterNodeId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(request => request.RequestedAtUtc, requestedAtUtc),
                stoppingToken
            ) > 0;

    /// <inheritdoc />
    public async Task<bool> RecordFailureAsync(
        Guid clusterNodeId,
        DateTimeOffset attemptedAtUtc,
        string error,
        CancellationToken stoppingToken = default
    ) =>
        await Set.Where(request => request.ClusterNodeId == clusterNodeId)
            .ExecuteUpdateAsync(
                setters =>
                    setters
                        .SetProperty(
                            request => request.AttemptCount,
                            request => request.AttemptCount + 1
                        )
                        .SetProperty(request => request.LastAttemptAtUtc, attemptedAtUtc)
                        .SetProperty(request => request.LastError, error),
                stoppingToken
            ) > 0;
}
