using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration lists where a persisted <c>WebhookDeliveryAttemptEntity</c> stands in its
///     delivery lifecycle. It is Ninlil vocabulary, not an external standard. All state changes
///     happen in <c>Server.Webhooks.WebhookDeliveryService</c> and
///     <c>WebhookDeliveryRetrySweepService</c>, whose doc comments give the rules.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<WebhookDeliveryAttemptStatusEnum>>]
public enum WebhookDeliveryAttemptStatusEnum
{
    /// <summary>
    ///     This delivery has not reached a terminal outcome. Either it has not been attempted yet (a
    ///     short-lived in-memory state that is never persisted; see
    ///     <c>WebhookDeliveryService.DeliverToSubscriptionAsync</c>), or its last attempt failed and a
    ///     retry is scheduled for <c>WebhookDeliveryAttemptEntity.NextRetryDueUtc</c>. No separate
    ///     "interrupted" state is needed: after a restart, the due-for-retry query in
    ///     <c>WebhookDeliveryRetrySweepService</c> finds a pending row whose retry time has passed and
    ///     resumes it.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Pending")]
    Pending = 0,

    /// <summary>
    ///     The subscriber's endpoint returned a 2xx response. Terminal: the row is never attempted
    ///     again.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Succeeded")]
    Succeeded = 1,

    /// <summary>
    ///     The delivery used up its <c>WebhookDeliveryAttemptEntity.MaxAttempts</c> without a
    ///     successful response, or its subscription was deleted before a scheduled retry. Terminal:
    ///     the row is never attempted again, but it stays on record so an operator can inspect it.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "DeadLettered")]
    DeadLettered = 2,
}
