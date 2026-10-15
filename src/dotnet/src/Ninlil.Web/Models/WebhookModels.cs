using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries the fields used to create a new outbound webhook subscription
///     (<c>POST /api/webhooks</c>). <see cref="WebhookSubscriptionCreateResponse" /> is the only
///     place <see cref="AuthCredentials" /> and the HMAC signing secret are ever returned.
/// </summary>
/// <param name="Name">An operator-chosen, human-readable, unique name for the subscription.</param>
/// <param name="TargetUrl">The absolute http or https URL every matching event is POSTed to.</param>
/// <param name="EventTypeNames">
///     The event types to deliver, by their <c>DnsServerEventTypeRegistry</c> name. Only the
///     seven types webhooks deliver are accepted: <c>DnsZoneChanged</c>, <c>DnsCacheEvicted</c>,
///     <c>DnssecZoneResigningCompleted</c>, <c>AcmeCertificateRenewalSucceeded</c>,
///     <c>AcmeCertificateRenewalFailed</c>, <c>CloudDnsZoneSyncFailed</c> and
///     <c>TrustAnchorRollover</c>; any other name returns 400.
/// </param>
/// <param name="AuthMode">The authentication the delivery POST uses, if any. Defaults to none.</param>
/// <param name="AuthCredentials">
///     The delivery POST's credentials, required when <see cref="AuthMode" /> is not <c>None</c>:
///     <c>{"BearerToken":"..."}</c> for <c>Bearer</c>, or
///     <c>{"Username":"...","Password":"..."}</c> for <c>Basic</c>. They always come from the
///     operator (the server never generates them) and are returned once, in this endpoint's
///     response.
/// </param>
/// <param name="EnableHmacSigning">
///     Whether deliveries are signed with an HMAC secret in the <c>X-Ninlil-Signature</c> header,
///     so the receiver can verify them.
/// </param>
/// <param name="HmacSigningSecret">
///     The HMAC signing secret, used only when <see cref="EnableHmacSigning" /> is
///     <see langword="true" />. When omitted, a random 32-byte secret is generated. Either way the
///     plaintext secret is shown once, in this endpoint's response. Neither <c>PUT</c> nor
///     <c>PATCH</c> can change it, as with a TSIG key's secret (see
///     <see cref="TsigKeyWriteRequest" />); to rotate or remove it, delete and recreate the
///     subscription.
/// </param>
/// <param name="MaxDeliveryAttempts">
///     How many times one event is attempted before the delivery is given up. Must be positive;
///     defaults to 5.
/// </param>
/// <param name="IsDisabled">Whether the subscription is disabled.</param>
/// <param name="Comments">Free-form operator notes about the subscription.</param>
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
///     settings and credentials (<c>PUT /api/webhooks/{id}</c>). Unlike a TSIG key's generated
///     secret, <see cref="AuthCredentials" /> comes from the operator, who already knows the value,
///     so a <c>PUT</c> replaces it too, as with <see cref="CloudProviderCredentialWriteRequest" />.
///     The HMAC signing secret (see <see cref="WebhookSubscriptionCreateRequest.HmacSigningSecret" />)
///     cannot be changed this way; delete and recreate the subscription instead.
/// </summary>
/// <param name="Name">The subscription's name.</param>
/// <param name="TargetUrl">The absolute URL every matching event is POSTed to.</param>
/// <param name="EventTypeNames">The event types to deliver, replacing the whole set. Only the seven types webhooks deliver are accepted; any other name returns 400.</param>
/// <param name="AuthMode">The authentication the delivery POST uses, if any.</param>
/// <param name="AuthCredentials">The delivery POST's credentials, replacing the stored ones.</param>
/// <param name="MaxDeliveryAttempts">How many times one event is attempted. Must be positive; defaults to 5.</param>
/// <param name="IsDisabled">Whether the subscription is disabled.</param>
/// <param name="Comments">Free-form operator notes about the subscription.</param>
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
///     This class carries a webhook subscription without its secrets, as returned by the list and
///     get endpoints and nested in <see cref="WebhookSubscriptionCreateResponse" />, the one
///     response that includes <see cref="WebhookSubscriptionCreateResponse.AuthCredentials" /> and
///     <see cref="WebhookSubscriptionCreateResponse.HmacSigningSecret" />.
/// </summary>
/// <param name="Id">The subscription's identifier.</param>
/// <param name="Name">The subscription's name.</param>
/// <param name="TargetUrl">The absolute URL every matching event is POSTed to.</param>
/// <param name="EventTypeNames">The event types the subscription receives.</param>
/// <param name="AuthMode">The authentication the delivery POST uses, if any.</param>
/// <param name="HasHmacSigningSecret">
///     Whether the subscription has an HMAC signing secret, so a caller can tell that signing is
///     on without the secret being exposed.
/// </param>
/// <param name="MaxDeliveryAttempts">How many times one event is attempted.</param>
/// <param name="IsDisabled">Whether the subscription is disabled.</param>
/// <param name="Comments">Free-form operator notes about the subscription.</param>
/// <param name="CreatedAtUtc">When the subscription was created.</param>
/// <param name="UpdatedAtUtc">When the subscription was last modified, or <see langword="null" /> if never.</param>
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
///     This class carries a newly created webhook subscription with its plaintext credentials
///     and, if signing is on, its plaintext HMAC signing secret. It is the only response that
///     includes either: like every secret-bearing entity, a secret is revealed once at creation.
/// </summary>
/// <param name="Subscription">The new subscription, without secrets.</param>
/// <param name="AuthCredentials">
///     The delivery POST's credentials, or <see langword="null" /> when
///     <see cref="WebhookSubscriptionDetail.AuthMode" /> is <c>None</c>.
/// </param>
/// <param name="HmacSigningSecret">
///     The HMAC signing secret, as supplied or generated, or <see langword="null" /> when signing
///     is off.
/// </param>
public record WebhookSubscriptionCreateResponse(
    WebhookSubscriptionDetail Subscription,
    IReadOnlyDictionary<string, string> AuthCredentials,
    string HmacSigningSecret
);

/// <summary>
///     This class carries one webhook delivery and its attempts so far, as returned by
///     <c>GET /api/webhooks/{id}/delivery-attempts</c> from the stored
///     <c>WebhookDeliveryAttemptEntity</c> history. It is read-only: only
///     <c>WebhookDeliveryService</c> and <c>WebhookDeliveryRetrySweepService</c> write deliveries,
///     as they make attempts.
/// </summary>
/// <param name="Id">The delivery's identifier.</param>
/// <param name="SubscriptionId">The identifier of the subscription the delivery is for.</param>
/// <param name="SubscriptionName">
///     The subscription's name when the delivery was created, so the record stays meaningful if
///     the subscription is renamed or deleted.
/// </param>
/// <param name="EventTypeName">The registered event type name the delivery carries (for example <c>"DnsZoneChanged"</c>).</param>
/// <param name="PayloadJson">
///     The JSON request body sent on every attempt. It is captured once, when the delivery is
///     created, so a retry, even after a restart, sends the same bytes.
/// </param>
/// <param name="Status">Where the delivery stands in its lifecycle.</param>
/// <param name="AttemptCount">How many attempts have been made so far.</param>
/// <param name="MaxAttempts">
///     The most attempts the delivery may make: the subscription's
///     <see cref="WebhookSubscriptionDetail.MaxDeliveryAttempts" /> when the delivery was created.
/// </param>
/// <param name="LastAttemptedAtUtc">When the last attempt was made, or <see langword="null" /> if none has been.</param>
/// <param name="NextRetryDueUtc">
///     When the next retry is due, or <see langword="null" /> once <see cref="Status" /> is final.
/// </param>
/// <param name="LastHttpStatusCode">
///     The HTTP status code the receiver returned to the last attempt, or <see langword="null" />
///     if that attempt failed before a response arrived.
/// </param>
/// <param name="LastErrorMessage">A short description of the last failure, or <see langword="null" /> if the last attempt succeeded.</param>
/// <param name="CompletedAtUtc">When the delivery reached a final status, or <see langword="null" /> while it is pending.</param>
/// <param name="CreatedAtUtc">When the delivery was first recorded.</param>
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
