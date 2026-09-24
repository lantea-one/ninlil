using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This interface extends <see cref="IRepository{TEntity}" /> with the real, pushed-down
///     lookups <c>Server.Webhooks</c>'s own delivery-retry sweep and retention cleanup need. A
///     long-running deployment can accumulate a genuinely large number of delivery-attempt rows
///     over time (every matching subscription's own delivery of every noteworthy event, for as
///     long as the server has been running), so -- mirroring <see cref="IDnsQueryLogRepository" />'s
///     own already-established precedent for exactly this same "can reach real scale" reasoning --
///     every method here is expected to translate into a real, filtered/indexed query against the
///     underlying storage engine, never a full table/collection scan materialized into memory
///     first.
/// </summary>
public interface IWebhookDeliveryAttemptRepository : IRepository<WebhookDeliveryAttemptEntity>
{
    /// <summary>
    ///     This method finds every delivery currently due for a retry attempt --
    ///     <see cref="WebhookDeliveryAttemptEntity.Status" /> still
    ///     <see cref="Common.Enum.WebhookDeliveryAttemptStatusEnum.Pending" /> and
    ///     <see cref="WebhookDeliveryAttemptEntity.NextRetryDueUtc" /> at or before
    ///     <paramref name="asOfUtc" />. This is the one query
    ///     <c>Server.Webhooks.WebhookDeliveryRetrySweepService</c>'s own periodic sweep (a plain
    ///     text reference, not <see langword="see cref" />, since this project sits below every
    ///     <c>Server.*</c> project in the dependency layering and cannot reference back up into
    ///     one of them) relies on for real restart durability: a row left behind by a process that
    ///     was killed
    ///     mid-backoff (or mid-attempt -- see <see cref="WebhookDeliveryAttemptEntity" />'s own
    ///     "at-least-once" doc comment) is, by construction, already sitting in exactly this state
    ///     the moment a fresh process's own sweep next runs, with no separate "was this delivery
    ///     interrupted" bookkeeping needed at all.
    /// </summary>
    /// <param name="asOfUtc">The moment to compare every candidate row's own due time against.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>Every delivery due for a retry attempt, in no particular order.</returns>
    Task<IReadOnlyList<WebhookDeliveryAttemptEntity>> ListDueForRetryAsync(
        DateTimeOffset asOfUtc,
        CancellationToken stoppingToken = default
    );

    /// <summary>
    ///     This method permanently removes every delivery that has already reached a terminal
    ///     outcome (<see cref="Common.Enum.WebhookDeliveryAttemptStatusEnum.Succeeded" /> or
    ///     <see cref="Common.Enum.WebhookDeliveryAttemptStatusEnum.DeadLettered" />) whose own
    ///     <see cref="WebhookDeliveryAttemptEntity.CompletedAtUtc" /> is older than
    ///     <paramref name="cutoffUtc" />, as a single bulk delete rather than loading each matching
    ///     row into memory first only to immediately discard it -- the same retention-sweep shape
    ///     <see cref="IDnsQueryLogRepository.DeleteOlderThanAsync" /> already establishes. A row
    ///     still <see cref="Common.Enum.WebhookDeliveryAttemptStatusEnum.Pending" /> is never
    ///     touched by this method regardless of age, since it may still genuinely be retried.
    /// </summary>
    /// <param name="cutoffUtc">The exclusive age cutoff; a terminal row completed at or after this moment survives.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The number of rows removed.</returns>
    Task<int> DeleteCompletedOlderThanAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken stoppingToken = default
    );
}
