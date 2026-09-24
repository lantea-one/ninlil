using MongoDB.Driver;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Repository;

/// <summary>
///     This class implements <see cref="IWebhookSubscriptionRepository" /> against MongoDB.
/// </summary>
/// <param name="context">The context to read collections from.</param>
/// <param name="changeTracker">The change tracker to stage writes onto.</param>
public class WebhookSubscriptionMongoRepository(
    NinlilDnsMongoContext context,
    MongoChangeTracker changeTracker
)
    : MongoRepositoryBase<WebhookSubscriptionEntity>(context.WebhookSubscriptions, changeTracker),
        IWebhookSubscriptionRepository
{
    /// <inheritdoc />
    public Task<WebhookSubscriptionEntity> FindByNameAsync(
        string name,
        CancellationToken stoppingToken = default
    ) =>
        Collection
            .Find(subscription => subscription.Name == name)
            .FirstOrDefaultAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<WebhookSubscriptionEntity>> ListEnabledAsync(
        CancellationToken stoppingToken = default
    ) => await Collection.Find(subscription => !subscription.IsDisabled).ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<WebhookSubscriptionEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Collection
            .Find(FilterDefinition<WebhookSubscriptionEntity>.Empty)
            .SortBy(subscription => subscription.Name)
            .Skip(skip)
            .Limit(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken stoppingToken = default) =>
        (int)
            await Collection.CountDocumentsAsync(
                FilterDefinition<WebhookSubscriptionEntity>.Empty,
                cancellationToken: stoppingToken
            );
}
