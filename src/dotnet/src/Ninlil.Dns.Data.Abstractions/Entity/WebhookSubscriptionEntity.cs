using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents a single outbound subscribable webhook -- a
///     "think SendGrid's Event Webhook" feature: one or more configurable POST-backs, fired
///     whenever one of a curated, "noteworthy" set of <c>IDnsServerEvent</c> types this
///     subscription has opted into actually occurs. <c>Server.Webhooks</c>'s delivery engine
///     reads this entity at delivery time; this project (<c>Data.Abstractions</c>) owns only the
///     schema, the event catalog, and this entity's own REST management surface.
/// </summary>
public class WebhookSubscriptionEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains an operator-chosen, human-readable, unique name for this
    ///     subscription.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     This property contains the absolute URL every matching event is POSTed to.
    /// </summary>
    public string TargetUrl { get; set; }

    /// <summary>
    ///     This property contains which event types this subscription wants delivered, by their
    ///     own registered <c>Server.Core</c> <c>DnsServerEventTypeRegistry</c> name (for example,
    ///     <c>"DnsZoneChanged"</c>) -- reusing that registry's own existing name strings rather
    ///     than inventing a second naming scheme. This
    ///     entity deliberately stores the name as a plain string, not a typed reference to that
    ///     registry: <c>Ninlil.Dns.Data.Abstractions</c> sits below every <c>Server.*</c>
    ///     project in this project's own dependency layering (see <c>Server.Core</c>'s own
    ///     "deliberately has no dependency on <c>Data.Abstractions</c>" invariant) and must never
    ///     reference back up into one of them -- the same soft-reference-by-string shape
    ///     <see cref="ZoneEntity.TsigKeyName" /> already establishes for a cross-entity reference
    ///     this layer cannot express as a real foreign key either.
    /// </summary>
    public List<string> EventTypeNames { get; set; } = [];

    /// <summary>
    ///     This property contains which authentication mode, if any, this subscription's own
    ///     outbound delivery POST uses.
    /// </summary>
    public WebhookAuthModeEnum AuthMode { get; set; } = WebhookAuthModeEnum.None;

    /// <summary>
    ///     This property contains this subscription's own outbound authentication credential(s),
    ///     encrypted at rest via <c>IDataProtector</c> -- the same "opaque payload, shape owned by
    ///     the consumer" pattern <see cref="CloudDnsProviderCredentialEntity.CredentialsEncrypted" />
    ///     already establishes. The decrypted plaintext is a small JSON object whose keys depend
    ///     on <see cref="AuthMode" /> (<c>{"BearerToken":"..."}</c> for <c>Bearer</c>, or
    ///     <c>{"Username":"...","Password":"..."}</c> for <c>Basic</c>), or <see langword="null" />
    ///     when <see cref="AuthMode" /> is <c>None</c>.
    /// </summary>
    public string AuthCredentialsEncrypted { get; set; }

    /// <summary>
    ///     This property contains this subscription's own optional HMAC payload-signing secret,
    ///     encrypted at rest via <c>IDataProtector</c>, or <see langword="null" /> when this
    ///     subscription does not sign its own outbound payloads at all. The delivery engine
    ///     computes the signature itself, over the exact outbound request body, at
    ///     delivery time -- this entity only stores the shared secret.
    /// </summary>
    public string HmacSigningSecretEncrypted { get; set; }

    /// <summary>
    ///     This property contains the maximum number of delivery attempts the delivery
    ///     engine makes for a single event before giving up and logging the failure -- a bounded
    ///     "some failover, not overly complex" retry budget, carried here as per-subscription
    ///     configuration since this entity holds the schema the delivery engine reads, not the
    ///     delivery engine itself.
    /// </summary>
    public int MaxDeliveryAttempts { get; set; } = 5;

    /// <summary>
    ///     This property indicates whether this subscription is currently disabled. A disabled
    ///     subscription is kept on record but never delivered to.
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    ///     This property contains free-form operator notes about this subscription.
    /// </summary>
    public string Comments { get; set; }
}
