using System.Linq.Expressions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Webhooks.Tests;

/// <summary>
///     This class is a read-only, in-memory <see cref="IWebhookSubscriptionRepository" /> for
///     <see cref="WebhookDeliveryServiceTests" /> and <see cref="WebhookDeliveryServiceHotReloadTests" />,
///     which test HTTP delivery and need no database. <see cref="ListEnabledAsync" /> filters
///     out disabled subscriptions like the real repositories, which
///     <c>DisabledSubscriptionIsNeverCalled</c> relies on. Write methods throw.
/// </summary>
/// <param name="subscriptions">The fixed set of subscriptions this fake repository serves.</param>
internal sealed class FakeWebhookSubscriptionRepository(
    IReadOnlyList<WebhookSubscriptionEntity> subscriptions
) : IWebhookSubscriptionRepository
{
    public Task<WebhookSubscriptionEntity> FindByNameAsync(
        string name,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult(
            subscriptions.FirstOrDefault(subscription =>
                string.Equals(subscription.Name, name, StringComparison.Ordinal)
            )
        );

    public Task<IReadOnlyList<WebhookSubscriptionEntity>> ListEnabledAsync(
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<WebhookSubscriptionEntity>>([
            .. subscriptions.Where(subscription => !subscription.IsDisabled),
        ]);

    public Task<WebhookSubscriptionEntity> GetByIdAsync(
        Guid id,
        CancellationToken stoppingToken = default
    ) => Task.FromResult(subscriptions.FirstOrDefault(subscription => subscription.Id == id));

    public Task<IReadOnlyList<WebhookSubscriptionEntity>> ListAsync(
        Expression<Func<WebhookSubscriptionEntity, bool>> predicate = null,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<WebhookSubscriptionEntity>>(
            predicate is null ? [.. subscriptions] : [.. subscriptions.Where(predicate.Compile())]
        );

    public Task<IReadOnlyList<WebhookSubscriptionEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    ) =>
        Task.FromResult<IReadOnlyList<WebhookSubscriptionEntity>>([
            .. subscriptions
                .OrderBy(subscription => subscription.Name, StringComparer.Ordinal)
                .Skip(skip)
                .Take(take),
        ]);

    public Task<int> CountAsync(CancellationToken stoppingToken = default) =>
        Task.FromResult(subscriptions.Count);

    public Task AddAsync(
        WebhookSubscriptionEntity entity,
        CancellationToken stoppingToken = default
    ) => throw new NotSupportedException("This fake repository is read-only.");

    public void Update(WebhookSubscriptionEntity entity) =>
        throw new NotSupportedException("This fake repository is read-only.");

    public void Remove(WebhookSubscriptionEntity entity) =>
        throw new NotSupportedException("This fake repository is read-only.");
}
