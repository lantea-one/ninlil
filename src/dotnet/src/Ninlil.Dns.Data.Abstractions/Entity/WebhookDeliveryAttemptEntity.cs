using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents a single persisted outbound webhook delivery -- one occurred event,
///     targeted at one <see cref="WebhookSubscriptionEntity" />, tracked from its very first
///     attempt through to whichever terminal outcome it eventually reaches
///     (<see cref="WebhookDeliveryAttemptStatusEnum.Succeeded" /> or
///     <see cref="WebhookDeliveryAttemptStatusEnum.DeadLettered" />). This is the real, on-disk
///     durability this project's outbound webhook feature previously lacked entirely -- before
///     this entity existed, a subscription's own retry/backoff loop lived purely in one detached
///     in-memory background task for the lifetime of a single process, so a restart mid-backoff
///     silently lost the delivery with no record it was ever attempted at all.
///     <para>
///         Unlike most of this project's own persisted entities, a single row here is genuinely
///         mutated in place several times over its own lifetime (each retry attempt updates the
///         same row rather than appending a new one) -- the same "state advances over time" shape
///         <c>Ninlil.Dhcp.Data.Abstractions.Entity.LeaseEntity</c> already establishes, which is why this class derives directly
///         from <see cref="EntityBase" /> rather than <see cref="AuditableEntityBase" />: this row's
///         own <see cref="LastAttemptedAtUtc" />/<see cref="CompletedAtUtc" /> already capture
///         every timing fact a caller could need, and this entity is neither operator-edited nor
///         (today) cluster-synced, so <see cref="AuditableEntityBase" />'s own
///         <c>UpdatedAtUtc</c>/<c>OriginatingNodeId</c> fields would carry no real meaning here.
///     </para>
///     <para>
///         <b>Delivery is at-least-once, never exactly-once</b> -- a deliberate, documented
///         tradeoff, not an oversight: if this process is killed after a subscriber's endpoint has
///         already returned a real success response but before that success was persisted back to
///         this row, the next sweep still finds the row <c>Pending</c> with an already-elapsed
///         <see cref="NextRetryDueUtc" /> and retries it, sending a second, duplicate POST. Every
///         real-world webhook system this feature is modeled after (Stripe's own Event Webhook
///         included) already expects a receiver to be idempotent for exactly this reason, so this
///         is treated as an accepted, correct consequence of genuine durability rather than a bug
///         to design around.
///     </para>
/// </summary>
public class WebhookDeliveryAttemptEntity : EntityBase
{
    /// <summary>
    ///     This property contains the identifier of the <see cref="WebhookSubscriptionEntity" />
    ///     this delivery targets -- a plain, soft-referenced <see cref="Guid" /> with no relational
    ///     foreign key or Mongo reference, the same convention
    ///     <see cref="ZoneChangeJournalEntryEntity.ZoneId" /> already establishes for a
    ///     cross-entity reference this layer cannot express identically across every supported
    ///     storage engine.
    /// </summary>
    public Guid SubscriptionId { get; set; }

    /// <summary>
    ///     This property contains the subscription's own name, snapshotted at the moment this
    ///     delivery was first created -- so a delivery-attempt record stays meaningful on its own
    ///     even after the subscription it originally targeted has since been renamed or deleted
    ///     entirely.
    /// </summary>
    public string SubscriptionName { get; set; }

    /// <summary>
    ///     This property contains the registered <c>Server.Core</c>
    ///     <c>DnsServerEventTypeRegistry</c> name of the event this delivery carries (for example,
    ///     <c>"DnsZoneChanged"</c>).
    /// </summary>
    public string EventTypeName { get; set; }

    /// <summary>
    ///     This property contains the exact outbound JSON request body this delivery sends (or
    ///     already sent) on every attempt -- captured once, at creation time, so that a retry
    ///     (whether performed by the same process moments later, or by a freshly restarted one
    ///     resuming this row days afterward) sends byte-identical content on every attempt rather
    ///     than re-serializing a domain event object that, after a restart, no longer exists in
    ///     memory at all.
    /// </summary>
    public string PayloadJson { get; set; }

    /// <summary>
    ///     This property contains where this delivery currently stands in its own lifecycle.
    /// </summary>
    public WebhookDeliveryAttemptStatusEnum Status { get; set; } =
        WebhookDeliveryAttemptStatusEnum.Pending;

    /// <summary>
    ///     This property contains how many delivery attempts have actually been made so far
    ///     (always at least 1 once this row has ever been persisted -- see this entity's own type
    ///     doc comment for why a row is never written before its first attempt completes).
    /// </summary>
    public int AttemptCount { get; set; }

    /// <summary>
    ///     This property contains the maximum number of delivery attempts this delivery may ever
    ///     make -- a snapshot of the target subscription's own
    ///     <see cref="WebhookSubscriptionEntity.MaxDeliveryAttempts" /> at the moment this row was
    ///     first created, so a later change to that subscription's own configured budget never
    ///     retroactively changes an already-in-flight delivery's own attempt ceiling.
    /// </summary>
    public int MaxAttempts { get; set; }

    /// <summary>
    ///     This property contains the UTC date and time of this delivery's own most recent attempt.
    /// </summary>
    public DateTimeOffset? LastAttemptedAtUtc { get; set; }

    /// <summary>
    ///     This property contains the UTC date and time this delivery's own next retry attempt is
    ///     due, or <see langword="null" /> once <see cref="Status" /> has reached a terminal
    ///     outcome. <c>Server.Webhooks.WebhookDeliveryRetrySweepService</c>'s own periodic sweep
    ///     is the sole mechanism that ever actually performs a second or later attempt (a plain
    ///     text reference, not <see langword="see cref" />, since this project sits below every
    ///     <c>Server.*</c> project in the dependency layering and cannot reference back up into
    ///     one of them) -- see that class's own doc comment for the real restart-durability
    ///     reasoning this design exists to provide.
    /// </summary>
    public DateTimeOffset? NextRetryDueUtc { get; set; }

    /// <summary>
    ///     This property contains the HTTP status code the subscriber's own endpoint returned on
    ///     this delivery's own most recent attempt, or <see langword="null" /> if that attempt
    ///     failed before a response was ever received (a connection failure, timeout, or similar
    ///     transport-level error).
    /// </summary>
    public int? LastHttpStatusCode { get; set; }

    /// <summary>
    ///     This property contains a short, human-readable description of this delivery's own most
    ///     recent failure, or <see langword="null" /> if its most recent (or only) attempt
    ///     succeeded.
    /// </summary>
    public string LastErrorMessage { get; set; }

    /// <summary>
    ///     This property contains the UTC date and time this delivery reached its own terminal
    ///     outcome (<see cref="WebhookDeliveryAttemptStatusEnum.Succeeded" /> or
    ///     <see cref="WebhookDeliveryAttemptStatusEnum.DeadLettered" />), or
    ///     <see langword="null" /> while <see cref="Status" /> is still
    ///     <see cref="WebhookDeliveryAttemptStatusEnum.Pending" />.
    /// </summary>
    public DateTimeOffset? CompletedAtUtc { get; set; }
}
