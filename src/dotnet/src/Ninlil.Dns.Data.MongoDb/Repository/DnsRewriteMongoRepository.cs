using MongoDB.Driver;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Repository;

/// <summary>
///     This class implements <see cref="IDnsRewriteRepository" /> against MongoDB.
/// </summary>
/// <param name="context">The context to read collections from.</param>
/// <param name="changeTracker">The change tracker to stage writes onto.</param>
public class DnsRewriteMongoRepository(
    NinlilDnsMongoContext context,
    MongoChangeTracker changeTracker
) : MongoRepositoryBase<DnsRewriteEntity>(context.DnsRewrites, changeTracker), IDnsRewriteRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsRewriteEntity>> ListEnabledAsync(
        CancellationToken stoppingToken = default
    ) => await Collection.Find(rewrite => !rewrite.IsDisabled).ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<DnsRewriteEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    )
    {
        SortDefinition<DnsRewriteEntity> domainThenCreatedAt =
            Builders<DnsRewriteEntity>.Sort.Combine(
                Builders<DnsRewriteEntity>.Sort.Ascending(rewrite => rewrite.Domain),
                Builders<DnsRewriteEntity>.Sort.Ascending(rewrite => rewrite.CreatedAtUtc)
            );

        return await Collection
            .Find(FilterDefinition<DnsRewriteEntity>.Empty)
            .Sort(domainThenCreatedAt)
            .Skip(skip)
            .Limit(take)
            .ToListAsync(stoppingToken);
    }

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken stoppingToken = default) =>
        (int)
            await Collection.CountDocumentsAsync(
                FilterDefinition<DnsRewriteEntity>.Empty,
                cancellationToken: stoppingToken
            );
}
