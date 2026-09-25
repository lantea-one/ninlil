using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookups the webhook
///     REST API and the delivery engine in <c>Server.Webhooks</c> need, and the paged listing
///     behind <c>WebhooksController.List</c>, which has no filters. Delivery attempts are listed
///     separately, through <see cref="IWebhookDeliveryAttemptRepository" />.
/// </summary>
public interface IWebhookSubscriptionRepository : IRepository<WebhookSubscriptionEntity>
{
    /// <summary>
    ///     This method looks up a webhook subscription by its exact name.
    /// </summary>
    /// <param name="name">The subscription name to look up.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching subscription, or <see langword="null" /> if none exists.</returns>
    Task<WebhookSubscriptionEntity> FindByNameAsync(
        string name,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists every enabled webhook subscription. For each event, the delivery engine
    ///     narrows this set by <see cref="WebhookSubscriptionEntity.EventTypeNames" />.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    Task<IReadOnlyList<WebhookSubscriptionEntity>> ListEnabledAsync(
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists one page of webhook subscriptions, enabled and disabled, ordered by
    ///     name and paged in the database.
    /// </summary>
    /// <param name="skip">The number of matching subscriptions to skip, for paging.</param>
    /// <param name="take">The maximum number of subscriptions to return.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The matching page of subscriptions, ordered by name.</returns>
    Task<IReadOnlyList<WebhookSubscriptionEntity>> SearchAsync(
        int skip,
        int take,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method counts every webhook subscription, for the total a paged response reports
    ///     next to <see cref="SearchAsync" />.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of webhook subscriptions.</returns>
    Task<int> CountAsync(CancellationToken stoppingToken = default);
}
