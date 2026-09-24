using MongoDB.Driver;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Data.MongoDb;
using Ninlil.Dns.Data.MongoDb.Repository;

// Define our namespace.
namespace Ninlil.Dhcp.Data.MongoDb.Repository;

/// <summary>
///     This class implements <see cref="IDhcpScopeCustomOptionRepository" /> against MongoDB.
/// </summary>
/// <param name="context">The context to read collections from.</param>
/// <param name="changeTracker">The change tracker to stage writes onto.</param>
public class DhcpScopeCustomOptionMongoRepository(
    NinlilDnsMongoContext context,
    MongoChangeTracker changeTracker
)
    : MongoRepositoryBase<DhcpScopeCustomOptionEntity>(
        context.DhcpScopeCustomOptions,
        changeTracker
    ),
        IDhcpScopeCustomOptionRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DhcpScopeCustomOptionEntity>> SearchByScopeIdAsync(
        Guid scopeId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Collection
            .Find(customOption => customOption.ScopeId == scopeId)
            .SortBy(customOption => customOption.OptionTag)
            .Skip(skip)
            .Limit(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountByScopeIdAsync(
        Guid scopeId,
        CancellationToken stoppingToken = default
    ) =>
        (int)
            await Collection.CountDocumentsAsync(
                customOption => customOption.ScopeId == scopeId,
                cancellationToken: stoppingToken
            );

    /// <inheritdoc />
    public async Task<IReadOnlyList<DhcpScopeCustomOptionEntity>> ListEnabledByScopeIdAsync(
        Guid scopeId,
        CancellationToken stoppingToken = default
    ) =>
        await Collection
            .Find(customOption => customOption.ScopeId == scopeId && !customOption.IsDisabled)
            .ToListAsync(stoppingToken);
}
