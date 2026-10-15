using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Events.Broker;

// Define our namespace.
namespace Ninlil.Dns.Server.Webhooks;

/// <summary>
///     This class is the catalog of the event types a webhook subscription can ask for: the
///     types <see cref="Extension.ServiceCollectionWebhooksExtension.AddNinlilDnsServerWebhooks" />
///     registers a <see cref="WebhookDeliverySubscriber{TEvent}" /> for. Other registered events
///     (per-query events, user and role events, DHCP events) never reach a webhook, so a
///     subscription naming one could never fire. <c>WebhooksController</c> checks every
///     subscription's event type names with <see cref="IsDeliverable" />, and the admin
///     console's webhook form offers the same seven types. The registrations in the extension
///     must match <see cref="EventTypes" />; a test enforces it.
/// </summary>
public static class WebhookEventCatalog
{
    /// <summary>
    ///     This property lists the deliverable event types, in the order the extension registers
    ///     them.
    /// </summary>
    public static IReadOnlyList<Type> EventTypes { get; } =
    [
        typeof(DnsZoneChangedEvent),
        typeof(DnsCacheEvictedEvent),
        typeof(DnssecZoneResigningCompletedEvent),
        typeof(AcmeCertificateRenewalSucceededEvent),
        typeof(AcmeCertificateRenewalFailedEvent),
        typeof(CloudDnsZoneSyncFailedEvent),
        typeof(TrustAnchorRolloverEvent),
    ];

    /// <summary>
    ///     This property lists the registry names (<c>DnsServerEventTypeRegistry</c>) of
    ///     <see cref="EventTypes" />, the names a subscription may use.
    /// </summary>
    public static IReadOnlyList<string> EventTypeNames { get; } =
    [
        .. EventTypes.Select(type =>
            DnsServerEventTypeRegistry.TryGetName(type, out string name)
                ? name
                : throw new InvalidOperationException(
                    $"{type.Name} is in the webhook catalog but not in the event type registry."
                )
        ),
    ];

    /// <summary>
    ///     This method says whether a webhook subscription may name an event type.
    /// </summary>
    /// <param name="eventTypeName">The event type's registry name, for example <c>DnsZoneChanged</c>.</param>
    /// <returns>True when events of that type are delivered to webhooks; false for any other name, including registered ones.</returns>
    public static bool IsDeliverable(string eventTypeName) =>
        eventTypeName is not null && EventTypeNames.Contains(eventTypeName, StringComparer.Ordinal);
}
