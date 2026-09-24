using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookups a webhook
///     subscription's own REST CRUD surface and <c>Server.Webhooks</c>'s delivery engine both
///     need -- plus the real, pushed-down paging <c>WebhooksController</c>'s own <c>List</c>
///     action lists the full subscription set through. Like <see cref="IZoneRepository" />, this
///     repository takes no search-criteria parameter: <c>WebhooksController</c>'s own <c>List</c>
///     action has never accepted a filter querystring parameter of any kind, only pagination, so
///     there is no real filter axis for <see cref="SearchAsync" /> to translate. This is distinct
///     from, and unrelated to, <c>IWebhookDeliveryAttemptRepository</c>'s own separate delivery-
///     attempt listing (<c>WebhooksController.ListDeliveryAttempts</c>), which this sub-phase does
///     not touch.
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
    ///     This method lists every currently enabled webhook subscription -- the set the delivery
    ///     engine filters down by each subscription's own
    ///     <see cref="WebhookSubscriptionEntity.EventTypeNames" /> for a single occurred event.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    Task<IReadOnlyList<WebhookSubscriptionEntity>> ListEnabledAsync(
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method lists one page of every webhook subscription this server knows about
    ///     (enabled and disabled alike), ordered by name -- a real, translated <c>ORDER BY</c>/
    ///     <c>OFFSET</c>/<c>LIMIT</c> (or the MongoDB equivalent) rather than an in-memory
    ///     sort/skip/take over a fully materialized <see cref="IRepository{TEntity}.ListAsync" />
    ///     result.
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
    ///     This method counts every webhook subscription this server knows about, without
    ///     materializing them -- the total <see cref="SearchAsync" />'s own paged response reports
    ///     alongside its current page.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The total number of webhook subscriptions.</returns>
    Task<int> CountAsync(CancellationToken stoppingToken = default);
}
