using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents an outbound webhook subscription, similar to SendGrid's Event
///     Webhook: a URL that receives a POST whenever one of the <c>IDnsServerEvent</c> types it
///     subscribes to occurs. The delivery engine in <c>Server.Webhooks</c> reads it at delivery
///     time, and <c>WebhooksController</c> manages it. Each delivery is recorded as a
///     <see cref="WebhookDeliveryAttemptEntity" />.
/// </summary>
public class WebhookSubscriptionEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains the operator-chosen, unique name of this subscription.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     This property contains the absolute URL every matching event is POSTed to.
    /// </summary>
    public string TargetUrl { get; set; }

    /// <summary>
    ///     This property contains the event types this subscription wants, by their name in
    ///     <c>Server.Core.Events.Broker.DnsServerEventTypeRegistry</c> (for example
    ///     <c>"DnsZoneChanged"</c>). They are plain strings because this project sits below the
    ///     <c>Server.*</c> projects and cannot reference them; <see cref="ZoneEntity.TsigKeyName" /> is
    ///     a string reference for a similar reason.
    /// </summary>
    public List<string> EventTypeNames { get; set; } = [];

    /// <summary>
    ///     This property contains the authentication mode, if any, of the delivery POST.
    /// </summary>
    public WebhookAuthModeEnum AuthMode { get; set; } = WebhookAuthModeEnum.None;

    /// <summary>
    ///     This property contains the delivery credentials, encrypted at rest with
    ///     <c>IDataProtector</c>, as <see cref="CloudDnsProviderCredentialEntity.CredentialsEncrypted" />
    ///     is. The plaintext is a small JSON object whose keys depend on <see cref="AuthMode" />
    ///     (<c>{"BearerToken":"..."}</c> for <c>Bearer</c>, <c>{"Username":"...","Password":"..."}</c>
    ///     for <c>Basic</c>), or <see langword="null" /> when <see cref="AuthMode" /> is <c>None</c>.
    /// </summary>
    public string AuthCredentialsEncrypted { get; set; }

    /// <summary>
    ///     This property contains the optional HMAC payload-signing secret, encrypted at rest with
    ///     <c>IDataProtector</c>, or <see langword="null" /> when payloads are not signed. The
    ///     delivery engine computes the signature over the request body at delivery time.
    /// </summary>
    public string HmacSigningSecretEncrypted { get; set; }

    /// <summary>
    ///     This property contains the maximum number of delivery attempts per event. When they are
    ///     used up, the delivery is dead-lettered
    ///     (<see cref="Common.Enum.WebhookDeliveryAttemptStatusEnum.DeadLettered" />).
    /// </summary>
    public int MaxDeliveryAttempts { get; set; } = 5;

    /// <summary>
    ///     This property indicates whether this subscription is disabled. A disabled subscription is
    ///     kept but never delivered to.
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    ///     This property contains free-form operator notes about this subscription.
    /// </summary>
    public string Comments { get; set; }
}
