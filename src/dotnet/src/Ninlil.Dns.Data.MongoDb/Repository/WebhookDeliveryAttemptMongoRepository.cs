using MongoDB.Driver;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Repository;

/// <summary>
///     This class implements <see cref="IWebhookDeliveryAttemptRepository" /> against MongoDB.
///     The due-for-retry lookup and the retention delete run as MongoDB filters and commands,
///     never in memory, because the collection can grow large; like
///     <see cref="DnsQueryLogMongoRepository" />, it creates its own indexes.
/// </summary>
public class WebhookDeliveryAttemptMongoRepository
    : MongoRepositoryBase<WebhookDeliveryAttemptEntity>,
        IWebhookDeliveryAttemptRepository
{
    /// <summary>
    ///     This field guards <see cref="EnsureIndexesFireAndForget" /> so only the first instance in
    ///     the process creates the indexes.
    /// </summary>
    private static int _indexesEnsured;

    /// <summary>
    ///     This constructor sets up the repository and, once per process, starts a best-effort
    ///     creation of its indexes.
    /// </summary>
    /// <param name="context">The context to read collections from.</param>
    /// <param name="changeTracker">The change tracker to stage writes onto.</param>
    public WebhookDeliveryAttemptMongoRepository(
        NinlilDnsMongoContext context,
        MongoChangeTracker changeTracker
    )
        : base(context.WebhookDeliveryAttempts, changeTracker)
    {
        EnsureIndexesFireAndForget();
    }

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

    /// <summary>
    ///     This method starts index creation once per process and ignores any failure, since a
    ///     missing index only slows queries.
    /// </summary>
    private void EnsureIndexesFireAndForget()
    {
        if (Interlocked.CompareExchange(ref _indexesEnsured, 1, 0) != 0)
            return;

        IMongoCollection<WebhookDeliveryAttemptEntity> collection = Collection;

        _ = Task.Run(async () =>
        {
            try
            {
                await collection.Indexes.CreateManyAsync([
                    new CreateIndexModel<WebhookDeliveryAttemptEntity>(
                        Builders<WebhookDeliveryAttemptEntity>
                            .IndexKeys.Ascending(attempt => attempt.Status)
                            .Ascending(attempt => attempt.NextRetryDueUtc)
                    ),
                    new CreateIndexModel<WebhookDeliveryAttemptEntity>(
                        Builders<WebhookDeliveryAttemptEntity>.IndexKeys.Ascending(attempt =>
                            attempt.CompletedAtUtc
                        )
                    ),
                    new CreateIndexModel<WebhookDeliveryAttemptEntity>(
                        Builders<WebhookDeliveryAttemptEntity>.IndexKeys.Ascending(attempt =>
                            attempt.SubscriptionId
                        )
                    ),
                ]);
            }
            catch
            {
                // Best effort only; see the method summary.
            }
        });
    }
}
