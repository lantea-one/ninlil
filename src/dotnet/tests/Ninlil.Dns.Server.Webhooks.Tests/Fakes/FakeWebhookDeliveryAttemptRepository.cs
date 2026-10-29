using System.Linq.Expressions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Webhooks.Tests.Fakes;

/// <summary>
///     This class is a list-backed <see cref="IWebhookDeliveryAttemptRepository" /> for the
///     delivery tests, which test the retry state machine and need no database. Its queries
///     match the EF Core repository's filters. <see cref="Items" /> is public so a test can
///     check what <see cref="WebhookDeliveryService" /> saved (its
///     <see cref="WebhookDeliveryAttemptEntity.Status" />,
///     <see cref="WebhookDeliveryAttemptEntity.NextRetryDueUtc" /> and so on).
/// </summary>
public sealed class FakeWebhookDeliveryAttemptRepository : IWebhookDeliveryAttemptRepository
{
    public readonly List<WebhookDeliveryAttemptEntity> Items = [];

    public Task<WebhookDeliveryAttemptEntity> GetByIdAsync(
        Guid id,
        CancellationToken stoppingToken = default
    ) => Task.FromResult(Items.FirstOrDefault(item => item.Id == id));

    public Task<IReadOnlyList<WebhookDeliveryAttemptEntity>> ListAsync(
        Expression<Func<WebhookDeliveryAttemptEntity, bool>> predicate = null,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<WebhookDeliveryAttemptEntity>>(
            predicate is null ? [.. Items] : [.. Items.Where(predicate.Compile())]
        );

    public Task AddAsync(
        WebhookDeliveryAttemptEntity entity,
        CancellationToken stoppingToken = default
    )
    {
        Items.Add(entity);
        return Task.CompletedTask;
    }

    public void Update(WebhookDeliveryAttemptEntity entity) { }

    public void Remove(WebhookDeliveryAttemptEntity entity) => Items.Remove(entity);

    /// <summary>
    ///     Gets the number of <see cref="ListDueForRetryAsync" /> calls, so a test can check how
    ///     many reads a sweep made.
    /// </summary>
    public int DueReadCount { get; private set; }

    /// <summary>
    ///     Gets the size limit passed to each <see cref="ListDueForRetryAsync" /> call, in order.
    /// </summary>
    public List<int> DueReadLimits { get; } = [];

    public Task<IReadOnlyList<WebhookDeliveryAttemptEntity>> ListDueForRetryAsync(
        DateTimeOffset asOfUtc,
        int maxCount,
        CancellationToken stoppingToken = default
    )
    {
        DueReadCount++;
        DueReadLimits.Add(maxCount);
        return Task.FromResult<IReadOnlyList<WebhookDeliveryAttemptEntity>>([
            .. Items
                .Where(item =>
                    item.Status == Ninlil.Dns.Common.Enum.WebhookDeliveryAttemptStatusEnum.Pending
                    && item.NextRetryDueUtc is not null
                    && item.NextRetryDueUtc <= asOfUtc
                )
                .OrderBy(item => item.NextRetryDueUtc)
                .ThenBy(item => item.Id)
                .Take(maxCount),
        ]);
    }

    public Task<int> DeleteCompletedOlderThanAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken stoppingToken = default
    )
    {
        int removed = Items.RemoveAll(item =>
            item.Status != Ninlil.Dns.Common.Enum.WebhookDeliveryAttemptStatusEnum.Pending
            && item.CompletedAtUtc is not null
            && item.CompletedAtUtc < cutoffUtc
        );
        return Task.FromResult(removed);
    }

    public Task<IReadOnlyList<WebhookDeliveryAttemptEntity>> SearchBySubscriptionIdAsync(
        Guid subscriptionId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<WebhookDeliveryAttemptEntity>>([
            .. Items
                .Where(item => item.SubscriptionId == subscriptionId)
                .OrderByDescending(item => item.CreatedAtUtc)
                .ThenByDescending(item => item.Id)
                .Skip(skip)
                .Take(take),
        ]);

    public Task<int> CountBySubscriptionIdAsync(
        Guid subscriptionId,
        CancellationToken stoppingToken = default
    ) => Task.FromResult(Items.Count(item => item.SubscriptionId == subscriptionId));
}
