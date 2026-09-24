using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries the fields used to create a new outbound webhook subscription
///     (<c>POST /api/webhooks</c>). See <see cref="WebhookSubscriptionCreateResponse" /> for the
///     one place <see cref="AuthCredentials" />/the resulting HMAC signing secret are ever shown.
/// </summary>
/// <param name="Name">An operator-chosen, human-readable, unique name for this subscription.</param>
/// <param name="TargetUrl">The absolute URL every matching event is POSTed to.</param>
/// <param name="EventTypeNames">
///     Which event types this subscription wants delivered, by their own registered
///     <c>DnsServerEventTypeRegistry</c> name (for example, <c>"DnsZoneChanged"</c>) -- validated
///     against that real registry before this subscription is ever persisted, rather than
///     accepting an arbitrary string, the same "validate against the real registry/parser"
///     discipline <see cref="ForwarderWriteRequest" />'s own <c>DefinitionString</c> already
///     applies.
/// </param>
/// <param name="AuthMode">Which authentication mode, if any, this subscription's own outbound delivery POST uses.</param>
/// <param name="AuthCredentials">
///     This subscription's own outbound authentication credential(s), required when
///     <see cref="AuthMode" /> is not <c>None</c> -- <c>{"BearerToken":"..."}</c> for
///     <c>Bearer</c>, or <c>{"Username":"...","Password":"..."}</c> for <c>Basic</c>. These are
///     always operator-supplied (this server never generates a bearer token or password on an
///     operator's behalf, unlike a TSIG key's own shared secret) and are returned exactly once,
///     in this endpoint's own response, and never again afterward.
/// </param>
/// <param name="EnableHmacSigning">
///     Whether this subscription signs its own outbound payloads with an HMAC secret, delivered
///     via a header the delivery engine adds (for example, <c>X-Ninlil-Signature</c>)
///     so the receiver can verify authenticity.
/// </param>
/// <param name="HmacSigningSecret">
///     This subscription's own HMAC payload-signing secret, used only when
///     <see cref="EnableHmacSigning" /> is <see langword="true" />. When omitted, a
///     cryptographically random secret is generated instead -- either way, the plaintext secret
///     is only ever shown once, in this endpoint's own response; every later request for this
///     subscription omits it. Once set, this secret can only be rotated or disabled by deleting
///     and recreating the subscription -- neither <c>PUT</c> nor <c>PATCH</c> can touch it, the
///     same reveal-once limitation <see cref="TsigKeyWriteRequest" /> already documents for a
///     TSIG key's own server-generated secret.
/// </param>
/// <param name="MaxDeliveryAttempts">
///     The maximum number of delivery attempts the delivery engine makes for a single
///     event before giving up and logging the failure.
/// </param>
/// <param name="IsDisabled">Whether the subscription starts out disabled.</param>
/// <param name="Comments">Free-form operator notes about this subscription.</param>
public record WebhookSubscriptionCreateRequest(
    string Name,
    string TargetUrl,
    List<string> EventTypeNames,
    WebhookAuthModeEnum AuthMode = WebhookAuthModeEnum.None,
    Dictionary<string, string> AuthCredentials = null,
    bool EnableHmacSigning = false,
    string HmacSigningSecret = null,
    int MaxDeliveryAttempts = 5,
    bool IsDisabled = false,
    string Comments = null
);

/// <summary>
///     This class carries the fields used to fully replace an existing webhook subscription's
///     metadata and auth credentials (<c>PUT /api/webhooks/{id}</c>). Unlike a TSIG key's own
///     server-generated shared secret, <see cref="AuthCredentials" /> is always operator-supplied
///     -- the caller already knows the exact plaintext value being submitted in this very request
///     -- so, exactly like <see cref="CloudProviderCredentialWriteRequest" />, a <c>PUT</c> may
///     replace it too, with no "how do we reveal a rotated value again" problem. This subscription's
///     own HMAC signing secret (see <see cref="WebhookSubscriptionCreateRequest.HmacSigningSecret" />)
///     cannot be touched this way at all -- delete and recreate the subscription instead.
/// </summary>
/// <param name="Name">The subscription's name.</param>
/// <param name="TargetUrl">The absolute URL every matching event is POSTed to.</param>
/// <param name="EventTypeNames">Which event types this subscription wants delivered, replacing the entire set.</param>
/// <param name="AuthMode">Which authentication mode, if any, this subscription's own outbound delivery POST uses.</param>
/// <param name="AuthCredentials">This subscription's own outbound authentication credential(s), replacing them entirely.</param>
/// <param name="MaxDeliveryAttempts">The maximum number of delivery attempts the delivery engine makes.</param>
/// <param name="IsDisabled">Whether the subscription is currently disabled.</param>
/// <param name="Comments">Free-form operator notes about this subscription.</param>
public record WebhookSubscriptionWriteRequest(
    string Name,
    string TargetUrl,
    List<string> EventTypeNames,
    WebhookAuthModeEnum AuthMode,
    Dictionary<string, string> AuthCredentials,
    int MaxDeliveryAttempts = 5,
    bool IsDisabled = false,
    string Comments = null
);

/// <summary>
///     This class carries a webhook subscription's own metadata, for list, single-item, and
///     (nested inside <see cref="WebhookSubscriptionCreateResponse" />) creation responses.
///     Neither the subscription's own auth credential(s) nor its HMAC signing secret are ever
///     included here -- see <see cref="WebhookSubscriptionCreateResponse" /> for the one place
///     <see cref="WebhookSubscriptionCreateResponse.AuthCredentials" />/
///     <see cref="WebhookSubscriptionCreateResponse.HmacSigningSecret" /> ever appear.
/// </summary>
/// <param name="Id">The subscription's own identifier.</param>
/// <param name="Name">The subscription's own name.</param>
/// <param name="TargetUrl">The absolute URL every matching event is POSTed to.</param>
/// <param name="EventTypeNames">Which event types this subscription is subscribed to.</param>
/// <param name="AuthMode">Which authentication mode, if any, this subscription's own outbound delivery POST uses.</param>
/// <param name="HasHmacSigningSecret">
///     Whether this subscription has an HMAC signing secret configured -- a plain boolean flag,
///     never the secret itself, so a caller can tell whether payload signing is active without
///     this response ever exposing the secret it would need to verify one.
/// </param>
/// <param name="MaxDeliveryAttempts">The maximum number of delivery attempts the delivery engine makes.</param>
/// <param name="IsDisabled">Whether this subscription is currently disabled.</param>
/// <param name="Comments">Free-form operator notes about this subscription.</param>
/// <param name="CreatedAtUtc">When this subscription was created.</param>
/// <param name="UpdatedAtUtc">When this subscription was last modified, or <see langword="null" /> if never.</param>
public record WebhookSubscriptionDetail(
    Guid Id,
    string Name,
    string TargetUrl,
    IReadOnlyList<string> EventTypeNames,
    WebhookAuthModeEnum AuthMode,
    bool HasHmacSigningSecret,
    int MaxDeliveryAttempts,
    bool IsDisabled,
    string Comments,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc
);

/// <summary>
///     This class carries a newly created webhook subscription's own metadata alongside its
///     plaintext auth credential(s) and, if enabled, its plaintext HMAC signing secret -- the
///     only response that will ever include either, per this project's reveal-once-then-omitted
///     exposure policy for every secret-bearing entity type.
/// </summary>
/// <param name="Subscription">The newly created subscription's own metadata.</param>
/// <param name="AuthCredentials">
///     The subscription's own outbound authentication credential(s), or <see langword="null" />
///     when <see cref="WebhookSubscriptionDetail.AuthMode" /> is <c>None</c>.
/// </param>
/// <param name="HmacSigningSecret">
///     The subscription's own HMAC signing secret (caller-supplied or freshly generated), or
///     <see langword="null" /> when signing was not enabled.
/// </param>
public record WebhookSubscriptionCreateResponse(
    WebhookSubscriptionDetail Subscription,
    IReadOnlyDictionary<string, string> AuthCredentials,
    string HmacSigningSecret
);

/// <summary>
///     This class carries a single persisted webhook delivery attempt's own current state, for
///     <c>GET /api/webhooks/{id}/delivery-attempts</c> -- the real, on-disk delivery history
///     <c>Server.Webhooks</c>'s own <c>WebhookDeliveryAttemptEntity</c> tracks, surfaced entirely
///     read-only. There is no corresponding write DTO: a delivery attempt is written exclusively
///     by <c>WebhookDeliveryService</c>/<c>WebhookDeliveryRetrySweepService</c> as a real delivery
///     is attempted, never authored or edited by an operator.
/// </summary>
/// <param name="Id">This delivery's own identifier.</param>
/// <param name="SubscriptionId">The identifier of the subscription this delivery targets.</param>
/// <param name="SubscriptionName">
///     The subscription's own name, snapshotted at the moment this delivery was first created --
///     so a delivery record stays meaningful even if the subscription it targeted has since been
///     renamed or deleted.
/// </param>
/// <param name="EventTypeName">The registered event type name this delivery carries (for example, <c>"DnsZoneChanged"</c>).</param>
/// <param name="PayloadJson">
///     The exact outbound JSON request body this delivery sends (or already sent) on every
///     attempt -- captured once, at creation time, so a retry (by the same process or a freshly
///     restarted one) always sends byte-identical content.
/// </param>
/// <param name="Status">Where this delivery currently stands in its own lifecycle.</param>
/// <param name="AttemptCount">How many delivery attempts have actually been made so far.</param>
/// <param name="MaxAttempts">
///     The maximum number of delivery attempts this delivery may ever make -- a snapshot of the
///     target subscription's own <see cref="WebhookSubscriptionDetail.MaxDeliveryAttempts" /> at
///     the moment this row was first created.
/// </param>
/// <param name="LastAttemptedAtUtc">When this delivery's own most recent attempt occurred, or <see langword="null" /> if never attempted yet.</param>
/// <param name="NextRetryDueUtc">
///     When this delivery's own next retry attempt is due, or <see langword="null" /> once
///     <see cref="Status" /> has reached a terminal outcome.
/// </param>
/// <param name="LastHttpStatusCode">
///     The HTTP status code the subscriber's own endpoint returned on this delivery's own most
///     recent attempt, or <see langword="null" /> if that attempt failed before a response was
///     ever received.
/// </param>
/// <param name="LastErrorMessage">A short, human-readable description of this delivery's own most recent failure, or <see langword="null" /> if its most recent (or only) attempt succeeded.</param>
/// <param name="CompletedAtUtc">When this delivery reached its own terminal outcome, or <see langword="null" /> while still pending.</param>
/// <param name="CreatedAtUtc">When this delivery was first recorded (its own first attempt).</param>
public record WebhookDeliveryAttemptDetail(
    Guid Id,
    Guid SubscriptionId,
    string SubscriptionName,
    string EventTypeName,
    string PayloadJson,
    WebhookDeliveryAttemptStatusEnum Status,
    int AttemptCount,
    int MaxAttempts,
    DateTimeOffset? LastAttemptedAtUtc,
    DateTimeOffset? NextRetryDueUtc,
    int? LastHttpStatusCode,
    string LastErrorMessage,
    DateTimeOffset? CompletedAtUtc,
    DateTimeOffset CreatedAtUtc
);
