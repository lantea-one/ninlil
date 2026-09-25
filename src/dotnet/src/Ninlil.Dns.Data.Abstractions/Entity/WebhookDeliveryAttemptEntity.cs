using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents one persisted webhook delivery: one event sent to one
///     <see cref="WebhookSubscriptionEntity" />, tracked from its first attempt to a terminal
///     outcome (<see cref="WebhookDeliveryAttemptStatusEnum.Succeeded" /> or
///     <see cref="WebhookDeliveryAttemptStatusEnum.DeadLettered" />). Keeping it on disk lets a
///     pending retry survive a restart. <c>Server.Webhooks.WebhookDeliveryService</c> creates
///     it after the first attempt and <c>WebhookDeliveryRetrySweepService</c> retries it.
///     <para>
///         Each retry updates the same row rather than adding one. It still derives from
///         <see cref="EntityBase" /> rather than <see cref="AuditableEntityBase" />:
///         <see cref="LastAttemptedAtUtc" /> and <see cref="CompletedAtUtc" /> hold the timing
///         that matters, and the row is neither edited by operators nor synced across a cluster,
///         so <c>UpdatedAtUtc</c> and <c>OriginatingNodeId</c> would mean nothing here.
///     </para>
///     <para>
///         <b>Delivery is at-least-once, not exactly-once.</b> If the process dies after the
///         subscriber returned success but before the row was saved, the next sweep finds it
///         <c>Pending</c> with a past <see cref="NextRetryDueUtc" /> and sends the POST again.
///         Webhook receivers (Stripe's included) are expected to be idempotent for this reason.
///     </para>
/// </summary>
public class WebhookDeliveryAttemptEntity : EntityBase
{
    /// <summary>
    ///     This property contains the identifier of the target <see cref="WebhookSubscriptionEntity" />.
    ///     It is a plain <see cref="Guid" /> with no foreign key, like
    ///     <see cref="ZoneChangeJournalEntryEntity.ZoneId" />, so the row outlives a deleted
    ///     subscription and the reference works the same on every storage engine.
    /// </summary>
    public Guid SubscriptionId { get; set; }

    /// <summary>
    ///     This property contains the subscription's name when this delivery was created, so the
    ///     record stays readable after the subscription is renamed or deleted.
    /// </summary>
    public string SubscriptionName { get; set; }

    /// <summary>
    ///     This property contains the <c>DnsServerEventTypeRegistry</c> name of the event this
    ///     delivery carries (for example <c>"DnsZoneChanged"</c>).
    /// </summary>
    public string EventTypeName { get; set; }

    /// <summary>
    ///     This property contains the JSON request body sent on every attempt. It is captured once
    ///     at creation, so every retry, including one by a restarted process, sends the same bytes;
    ///     after a restart the event object no longer exists to serialize again.
    /// </summary>
    public string PayloadJson { get; set; }

    /// <summary>
    ///     This property contains where this delivery stands in its lifecycle.
    /// </summary>
    public WebhookDeliveryAttemptStatusEnum Status { get; set; } =
        WebhookDeliveryAttemptStatusEnum.Pending;

    /// <summary>
    ///     This property contains how many delivery attempts have been made. It is at least 1 in
    ///     any saved row, because the row is first written after the first attempt.
    /// </summary>
    public int AttemptCount { get; set; }

    /// <summary>
    ///     This property contains the maximum number of attempts for this delivery: a copy of the
    ///     subscription's <see cref="WebhookSubscriptionEntity.MaxDeliveryAttempts" /> when the row
    ///     was created, so changing the subscription later does not affect deliveries in flight.
    /// </summary>
    public int MaxAttempts { get; set; }

    /// <summary>
    ///     This property contains the UTC date and time of this delivery's most recent attempt.
    /// </summary>
    public DateTimeOffset? LastAttemptedAtUtc { get; set; }

    /// <summary>
    ///     This property contains the UTC date and time the next retry is due, or
    ///     <see langword="null" /> once <see cref="Status" /> is terminal. Only the periodic sweep in
    ///     <c>Server.Webhooks.WebhookDeliveryRetrySweepService</c> makes the second and later
    ///     attempts.
    /// </summary>
    public DateTimeOffset? NextRetryDueUtc { get; set; }

    /// <summary>
    ///     This property contains the HTTP status code the subscriber returned on the most recent
    ///     attempt, or <see langword="null" /> if that attempt failed before a response (a
    ///     connection failure, timeout or similar).
    /// </summary>
    public int? LastHttpStatusCode { get; set; }

    /// <summary>
    ///     This property contains a short description of the most recent failure, or
    ///     <see langword="null" /> if the most recent attempt succeeded.
    /// </summary>
    public string LastErrorMessage { get; set; }

    /// <summary>
    ///     This property contains the UTC date and time this delivery reached its terminal
    ///     outcome (<see cref="WebhookDeliveryAttemptStatusEnum.Succeeded" /> or
    ///     <see cref="WebhookDeliveryAttemptStatusEnum.DeadLettered" />), or
    ///     <see langword="null" /> while <see cref="Status" /> is still
    ///     <see cref="WebhookDeliveryAttemptStatusEnum.Pending" />.
    /// </summary>
    public DateTimeOffset? CompletedAtUtc { get; set; }
}
