using MongoDB.Driver;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Repository;

/// <summary>
///     This class implements <see cref="IDnsAccessControlRuleRepository" /> against MongoDB.
/// </summary>
/// <param name="context">The context to read collections from.</param>
/// <param name="changeTracker">The change tracker to stage writes onto.</param>
public class DnsAccessControlRuleMongoRepository(
    NinlilDnsMongoContext context,
    MongoChangeTracker changeTracker
)
    : MongoRepositoryBase<DnsAccessControlRuleEntity>(context.DnsAccessControlRules, changeTracker),
        IDnsAccessControlRuleRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsAccessControlRuleEntity>> ListEnabledAsync(
        CancellationToken stoppingToken = default
    ) => await Collection.Find(rule => !rule.IsDisabled).ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsAccessControlRuleEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Collection
            .Find(FilterDefinition<DnsAccessControlRuleEntity>.Empty)
            .SortBy(rule => rule.Name)
            .Skip(skip)
            .Limit(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken stoppingToken = default) =>
        (int)
            await Collection.CountDocumentsAsync(
                FilterDefinition<DnsAccessControlRuleEntity>.Empty,
                cancellationToken: stoppingToken
            );
}
