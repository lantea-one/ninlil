using MongoDB.Driver;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Repository;

/// <summary>
///     This class implements <see cref="IWebhookDeliveryAttemptRepository" /> against MongoDB.
///     The due-for-retry lookup and the retention delete run as MongoDB filters and commands,
///     never in memory, because the collection can grow large. Its indexes are created at
///     startup by <see cref="Maintenance.MongoIndexCatalog" />.
/// </summary>
public class WebhookDeliveryAttemptMongoRepository
    : MongoRepositoryBase<WebhookDeliveryAttemptEntity>,
        IWebhookDeliveryAttemptRepository
{
    /// <summary>
    ///     This constructor sets up the repository.
    /// </summary>
    /// <param name="context">The context to read collections from.</param>
    /// <param name="changeTracker">The change tracker to stage writes onto.</param>
    public WebhookDeliveryAttemptMongoRepository(
        NinlilDnsMongoContext context,
        MongoChangeTracker changeTracker
    )
        : base(context.WebhookDeliveryAttempts, changeTracker) { }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WebhookDeliveryAttemptEntity>> ListDueForRetryAsync(
        DateTimeOffset asOfUtc,
        CancellationToken stoppingToken = default
    ) =>
        await Collection
            .Find(attempt =>
                attempt.Status == WebhookDeliveryAttemptStatusEnum.Pending
                && attempt.NextRetryDueUtc != null
                && attempt.NextRetryDueUtc <= asOfUtc
            )
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> DeleteCompletedOlderThanAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken stoppingToken = default
    )
    {
        DeleteResult result = await Collection.DeleteManyAsync(
            attempt =>
                attempt.Status != WebhookDeliveryAttemptStatusEnum.Pending
                && attempt.CompletedAtUtc != null
                && attempt.CompletedAtUtc < cutoffUtc,
            stoppingToken
        );
        return (int)result.DeletedCount;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WebhookDeliveryAttemptEntity>> SearchBySubscriptionIdAsync(
        Guid subscriptionId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        await Collection
            .Find(attempt => attempt.SubscriptionId == subscriptionId)
            .SortByDescending(attempt => attempt.CreatedAtUtc)
            .ThenByDescending(attempt => attempt.Id)
            .Skip(skip)
            .Limit(take)
            .ToListAsync(stoppingToken);

    /// <inheritdoc />
    public async Task<int> CountBySubscriptionIdAsync(
        Guid subscriptionId,
        CancellationToken stoppingToken = default
    ) =>
        (int)
            await Collection.CountDocumentsAsync(
                attempt => attempt.SubscriptionId == subscriptionId,
                cancellationToken: stoppingToken
            );
}
