using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the lookups the webhook
///     retry sweep and retention cleanup in <c>Server.Webhooks</c> need. A long-running server
///     can collect many delivery rows (one per matching subscription per event), so, as with
///     <see cref="IDnsQueryLogRepository" />, every method here must run as a filtered, indexed
///     query in the database, never a full scan loaded into memory.
/// </summary>
public interface IWebhookDeliveryAttemptRepository : IRepository<WebhookDeliveryAttemptEntity>
{
    /// <summary>
    ///     This method finds up to <paramref name="maxCount" /> deliveries due for a retry:
    ///     <see cref="WebhookDeliveryAttemptEntity.Status" /> is
    ///     <see cref="Common.Enum.WebhookDeliveryAttemptStatusEnum.Pending" /> and
    ///     <see cref="WebhookDeliveryAttemptEntity.NextRetryDueUtc" /> is at or before
    ///     <paramref name="asOfUtc" />, the oldest due first (by
    ///     <see cref="WebhookDeliveryAttemptEntity.NextRetryDueUtc" />; rows due at the same moment
    ///     come in no particular order). The periodic sweep in
    ///     <c>Server.Webhooks.WebhookDeliveryRetrySweepService</c> relies on it to survive restarts:
    ///     a row left by a process killed during a backoff (or an attempt; see the at-least-once
    ///     note on <see cref="WebhookDeliveryAttemptEntity" />) is already in this state when the
    ///     next process's sweep runs, so no "interrupted" bookkeeping is needed.
    ///     <para>
    ///         The limit keeps a backlog (after a long outage there can be tens of thousands of due
    ///         rows) out of memory. A caller that wants every due row calls again after it has moved
    ///         the rows it got out of the due state, as the sweep does; a row it leaves due is
    ///         returned again, first, so the caller must not loop on rows it cannot move.
    ///     </para>
    /// </summary>
    /// <param name="asOfUtc">The moment to compare each row's due time against.</param>
    /// <param name="maxCount">The most rows to return; a value below one returns none.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>Up to <paramref name="maxCount" /> deliveries due for a retry attempt, oldest due first.</returns>
    Task<IReadOnlyList<WebhookDeliveryAttemptEntity>> ListDueForRetryAsync(
        DateTimeOffset asOfUtc,
        int maxCount,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method deletes every delivery with a terminal outcome
    ///     (<see cref="Common.Enum.WebhookDeliveryAttemptStatusEnum.Succeeded" /> or
    ///     <see cref="Common.Enum.WebhookDeliveryAttemptStatusEnum.DeadLettered" />) whose
    ///     <see cref="WebhookDeliveryAttemptEntity.CompletedAtUtc" /> is before
    ///     <paramref name="cutoffUtc" />, as one bulk delete without loading the rows, like
    ///     <see cref="IDnsQueryLogRepository.DeleteOlderThanAsync" />. A
    ///     <see cref="Common.Enum.WebhookDeliveryAttemptStatusEnum.Pending" /> row is never deleted,
    ///     whatever its age, since it may still be retried.
    /// </summary>
    /// <param name="cutoffUtc">The exclusive cutoff; a terminal row completed at or after this moment is kept.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The number of rows removed.</returns>
    Task<int> DeleteCompletedOlderThanAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method returns one page of the deliveries of a subscription, newest first
    ///     (<c>CreatedAtUtc</c> descending, then <c>Id</c> descending), sorted and sliced in the
    ///     database. <c>WebhooksController</c> serves the delivery-attempts list with it.
    /// </summary>
    /// <param name="subscriptionId">The subscription whose deliveries to list.</param>
    /// <param name="skip">The number of rows to skip.</param>
    /// <param name="take">The maximum number of rows to return.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The requested page.</returns>
    Task<IReadOnlyList<WebhookDeliveryAttemptEntity>> SearchBySubscriptionIdAsync(
        Guid subscriptionId,
        int skip,
        int take,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method counts the deliveries of a subscription, the total behind
    ///     <see cref="SearchBySubscriptionIdAsync" />.
    /// </summary>
    /// <param name="subscriptionId">The subscription whose deliveries to count.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The number of deliveries of the subscription.</returns>
    Task<int> CountBySubscriptionIdAsync(
        Guid subscriptionId,
        CancellationToken stoppingToken = default
    );
}
