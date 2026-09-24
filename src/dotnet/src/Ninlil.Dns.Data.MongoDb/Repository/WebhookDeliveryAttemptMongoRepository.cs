using MongoDB.Driver;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Repository;

/// <summary>
///     This class implements <see cref="IWebhookDeliveryAttemptRepository" /> against MongoDB,
///     backing the retry sweep's own due-for-retry lookup and the retention sweep's own bulk
///     delete with real MongoDB filter documents/commands, never an in-memory scan of an
///     already-fetched collection -- mirroring <see cref="DnsQueryLogMongoRepository" />'s own
///     established reasoning for a collection that can genuinely grow large.
/// </summary>
public class WebhookDeliveryAttemptMongoRepository
    : MongoRepositoryBase<WebhookDeliveryAttemptEntity>,
        IWebhookDeliveryAttemptRepository
{
    /// <summary>
    ///     This field guards <see cref="EnsureIndexesFireAndForget" /> so that only the very
    ///     first repository instance constructed in this process actually issues the
    ///     index-creation commands, mirroring <see cref="DnsQueryLogMongoRepository" />'s own
    ///     identical guard.
    /// </summary>
    private static int _indexesEnsured;

    /// <summary>
    ///     This constructor wires this repository onto its own collection and change tracker,
    ///     then kicks off a best-effort, once-per-process attempt to ensure its own collection
    ///     indexes exist.
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
    ///     This method kicks off collection index creation exactly once per process, swallowing
    ///     any failure -- a missing index degrades this repository's own query performance, it
    ///     never makes a query incorrect, mirroring <see cref="DnsQueryLogMongoRepository" />'s
    ///     own identical reasoning.
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
                // Best effort only -- see this method's own doc comment.
            }
        });
    }
}
