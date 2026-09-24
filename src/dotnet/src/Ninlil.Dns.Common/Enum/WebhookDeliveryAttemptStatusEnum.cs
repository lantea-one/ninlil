using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration lists where a single persisted <c>WebhookDeliveryAttemptEntity</c>
///     currently stands in its own delivery lifecycle -- Ninlil's own operational vocabulary, not
///     an IANA-assigned or otherwise externally standardized value. Every real state transition
///     this enumeration's own values participate in is driven entirely by
///     <c>Server.Webhooks.WebhookDeliveryService</c>/<c>WebhookDeliveryRetrySweepService</c>; see
///     those classes' own doc comments for the exact rules.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<WebhookDeliveryAttemptStatusEnum>>]
public enum WebhookDeliveryAttemptStatusEnum
{
    /// <summary>
    ///     This delivery has not yet reached a terminal outcome: either it has never been
    ///     attempted yet (a transient in-memory state that is never itself persisted -- see
    ///     <c>WebhookDeliveryService.DeliverToSubscriptionAsync</c>'s own doc comment), or its most
    ///     recent attempt failed and a further retry is scheduled for
    ///     <c>WebhookDeliveryAttemptEntity.NextRetryDueUtc</c>. A row in this state surviving a
    ///     process restart, with its own <c>NextRetryDueUtc</c> already in the past, is exactly
    ///     what a resumed, interrupted delivery looks like on disk -- no separate "interrupted"
    ///     state is needed, since <c>WebhookDeliveryRetrySweepService</c>'s own due-for-retry query
    ///     naturally finds it again regardless of which process (the one that started it, or a
    ///     freshly restarted one) ends up actually resuming it.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Pending")]
    Pending = 0,

    /// <summary>
    ///     This delivery reached a 2xx response from the subscriber's own endpoint. Terminal --
    ///     this row is never attempted again.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Succeeded")]
    Succeeded = 1,

    /// <summary>
    ///     This delivery's own configured <c>WebhookDeliveryAttemptEntity.MaxAttempts</c> budget
    ///     was fully exhausted without ever receiving a successful response, or the subscription
    ///     it targeted was deleted before a scheduled retry could run. Terminal -- this row is
    ///     never attempted again, but (unlike this project's own purely in-memory retry behavior
    ///     before this entity existed) it remains on record as a real, inspectable dead letter
    ///     rather than simply vanishing once the last attempt's own failure was logged.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "DeadLettered")]
    DeadLettered = 2,
}
