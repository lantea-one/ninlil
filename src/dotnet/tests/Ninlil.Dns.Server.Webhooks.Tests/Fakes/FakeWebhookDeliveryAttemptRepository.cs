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

    public Task<IReadOnlyList<WebhookDeliveryAttemptEntity>> ListDueForRetryAsync(
        DateTimeOffset asOfUtc,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<WebhookDeliveryAttemptEntity>>([
            .. Items.Where(item =>
                item.Status == Ninlil.Dns.Common.Enum.WebhookDeliveryAttemptStatusEnum.Pending
                && item.NextRetryDueUtc is not null
                && item.NextRetryDueUtc <= asOfUtc
            ),
        ]);

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
}
