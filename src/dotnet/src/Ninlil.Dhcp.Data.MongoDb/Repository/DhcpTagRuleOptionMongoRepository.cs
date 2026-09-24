using MongoDB.Driver;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Data.MongoDb;
using Ninlil.Dns.Data.MongoDb.Repository;

// Define our namespace.
namespace Ninlil.Dhcp.Data.MongoDb.Repository;

/// <summary>
///     This class implements <see cref="IDhcpTagRuleOptionRepository" /> against MongoDB.
/// </summary>
/// <param name="context">The context to read collections from.</param>
/// <param name="changeTracker">The change tracker to stage writes onto.</param>
public class DhcpTagRuleOptionMongoRepository(
    NinlilDnsMongoContext context,
    MongoChangeTracker changeTracker
)
    : MongoRepositoryBase<DhcpTagRuleOptionEntity>(context.DhcpTagRuleOptions, changeTracker),
        IDhcpTagRuleOptionRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DhcpTagRuleOptionEntity>> SearchByTagRuleIdAsync(
        Guid tagRuleId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Collection
            .Find(ruleOption => ruleOption.TagRuleId == tagRuleId)
            .SortBy(ruleOption => ruleOption.OptionTag)
            .Skip(skip)
            .Limit(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountByTagRuleIdAsync(
        Guid tagRuleId,
        CancellationToken stoppingToken = default
    ) =>
        (int)
            await Collection.CountDocumentsAsync(
                ruleOption => ruleOption.TagRuleId == tagRuleId,
                cancellationToken: stoppingToken
            );

    /// <inheritdoc />
    public async Task<IReadOnlyList<DhcpTagRuleOptionEntity>> ListByTagRuleIdAsync(
        Guid tagRuleId,
        CancellationToken stoppingToken = default
    ) =>
        await Collection
            .Find(ruleOption => ruleOption.TagRuleId == tagRuleId)
            .ToListAsync(stoppingToken);
}
