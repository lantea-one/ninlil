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
    ///     This method finds every delivery due for a retry: <see cref="WebhookDeliveryAttemptEntity.Status" />
    ///     is <see cref="Common.Enum.WebhookDeliveryAttemptStatusEnum.Pending" /> and
    ///     <see cref="WebhookDeliveryAttemptEntity.NextRetryDueUtc" /> is at or before
    ///     <paramref name="asOfUtc" />. The periodic sweep in
    ///     <c>Server.Webhooks.WebhookDeliveryRetrySweepService</c> relies on it to survive restarts:
    ///     a row left by a process killed during a backoff (or an attempt; see the at-least-once
    ///     note on <see cref="WebhookDeliveryAttemptEntity" />) is already in this state when the
    ///     next process's sweep runs, so no "interrupted" bookkeeping is needed.
    /// </summary>
    /// <param name="asOfUtc">The moment to compare each row's due time against.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>Every delivery due for a retry attempt, in no particular order.</returns>
    Task<IReadOnlyList<WebhookDeliveryAttemptEntity>> ListDueForRetryAsync(
        DateTimeOffset asOfUtc,
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
}
