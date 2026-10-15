using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.Webhooks;

/// <summary>
///     This class is the <see cref="IDnsServerEventSubscriber{TEvent}" /> for webhooks,
///     registered once for each of the seven webhook event types (<c>DnsZoneChangedEvent</c>,
///     <c>DnsCacheEvictedEvent</c>, <c>DnssecZoneResigningCompletedEvent</c>,
///     <c>AcmeCertificateRenewalSucceededEvent</c>, <c>AcmeCertificateRenewalFailedEvent</c>,
///     <c>CloudDnsZoneSyncFailedEvent</c>, <c>TrustAnchorRolloverEvent</c>; see
///     <c>ServiceCollectionWebhooksExtension</c>); <see cref="WebhookEventCatalog" /> lists
///     them and is what a subscription's event type names are checked against. One generic class is enough because
///     <see cref="Ninlil.Dns.Server.Core.Events.DnsServerEventDispatcher" /> resolves subscribers
///     for an event's exact runtime type.
///     <para>
///         <see cref="HandleAsync" /> starts delivery on a detached task and returns at once,
///         like <c>ZoneNotifyEventSubscriber</c> and <c>CaaRecordReconciliationEventSubscriber</c>:
///         the dispatcher handles one event at a time, and a slow or unreachable endpoint must
///         not hold up other subscribers and events. The task opens its own dependency injection
///         scope for the scoped <see cref="WebhookDeliveryService" />, because the dispatcher's
///         scope is disposed once every subscriber has returned, before delivery finishes.
///     </para>
/// </summary>
/// <typeparam name="TEvent">The webhook-eligible event type this closed generic subscriber handles.</typeparam>
/// <param name="scopeFactory">The factory used to create a scope, and a <see cref="WebhookDeliveryService" />, per event.</param>
/// <param name="logger">The logger this subscriber records unhandled delivery-engine failures to.</param>
public sealed class WebhookDeliverySubscriber<TEvent>(
    IServiceScopeFactory scopeFactory,
    ILogger<WebhookDeliverySubscriber<TEvent>> logger
) : IDnsServerEventSubscriber<TEvent>
    where TEvent : IDnsServerEvent
{
    /// <inheritdoc />
    public ValueTask HandleAsync(TEvent domainEvent, CancellationToken stoppingToken)
    {
        _ = DeliverAsync(domainEvent, stoppingToken);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    ///     This method opens a scope and calls <see cref="WebhookDeliveryService.DeliverAsync" />.
    ///     HTTP failures are recorded per delivery there, so this catch only logs other failures,
    ///     such as resolving services, reading subscriptions, decrypting credentials or saving
    ///     the delivery.
    /// </summary>
    private async Task DeliverAsync(TEvent domainEvent, CancellationToken stoppingToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            WebhookDeliveryService deliveryService =
                scope.ServiceProvider.GetRequiredService<WebhookDeliveryService>();

            await deliveryService.DeliverAsync(domainEvent, stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Unhandled exception while delivering webhooks for a {EventType} event.",
                typeof(TEvent).Name
            );
        }
    }
}
