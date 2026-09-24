using MongoDB.Driver;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Data.MongoDb;
using Ninlil.Dns.Data.MongoDb.Repository;

// Define our namespace.
namespace Ninlil.Dhcp.Data.MongoDb.Repository;

/// <summary>
///     This class implements <see cref="IDhcpTagRuleRepository" /> against MongoDB.
/// </summary>
/// <param name="context">The context to read collections from.</param>
/// <param name="changeTracker">The change tracker to stage writes onto.</param>
public class DhcpTagRuleMongoRepository(
    NinlilDnsMongoContext context,
    MongoChangeTracker changeTracker
)
    : MongoRepositoryBase<DhcpTagRuleEntity>(context.DhcpTagRules, changeTracker),
        IDhcpTagRuleRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DhcpTagRuleEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Collection
            .Find(FilterDefinition<DhcpTagRuleEntity>.Empty)
            .SortBy(rule => rule.Name)
            .Skip(skip)
            .Limit(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken stoppingToken = default) =>
        (int)
            await Collection.CountDocumentsAsync(
                FilterDefinition<DhcpTagRuleEntity>.Empty,
                cancellationToken: stoppingToken
            );

    /// <inheritdoc />
    public async Task<IReadOnlyList<DhcpTagRuleEntity>> ListEnabledForScopeAsync(
        Guid scopeId,
        CancellationToken stoppingToken = default
    ) =>
        await Collection
            .Find(rule => !rule.IsDisabled && (rule.ScopeId == scopeId || rule.ScopeId == null))
            .ToListAsync(stoppingToken);
}
