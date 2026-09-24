using System.Linq.Expressions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

namespace Ninlil.Dns.Server.Webhooks.Tests.Fakes;

/// <summary>
///     This class is a minimal, in-memory <see cref="IWebhookDeliveryAttemptRepository" />
///     stand-in used by this project's own unit tests -- <see cref="WebhookDeliveryService" />
///     itself has no dependency on any particular storage engine, so a real EF Core/MongoDB
///     repository would only add unrelated database setup to a test that is actually about the
///     delivery/retry state machine. <see cref="Items" /> is exposed directly so a test can
///     inspect exactly what this engine persisted (its own <see cref="WebhookDeliveryAttemptEntity.Status" />,
///     <see cref="WebhookDeliveryAttemptEntity.NextRetryDueUtc" />, and so on) after a call.
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
