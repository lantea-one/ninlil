using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.Webhooks;

/// <summary>
///     This class is the single, generic <see cref="IDnsServerEventSubscriber{TEvent}" />
///     registered once per webhook-eligible event type -- one closed generic registration per
///     type (see <c>ServiceCollectionWebhooksExtension</c>) rather than seven hand-written,
///     near-identical subscriber classes, since <see cref="Ninlil.Dns.Server.Core.Events.DnsServerEventDispatcher" />
///     itself only ever resolves subscribers for an event's own exact concrete runtime type (via
///     <c>IDnsServerEventSubscriber&lt;&gt;.MakeGenericType</c>), which a single open generic
///     class satisfies perfectly well once closed over each of the 7 curated "noteworthy" event
///     types (<c>DnsZoneChangedEvent</c>, <c>DnsCacheEvictedEvent</c>,
///     <c>DnssecZoneResigningCompletedEvent</c>, <c>AcmeCertificateRenewalSucceededEvent</c>,
///     <c>AcmeCertificateRenewalFailedEvent</c>, <c>CloudDnsZoneSyncFailedEvent</c>,
///     <c>TrustAnchorRolloverEvent</c>).
///     <para>
///         Delivery runs in a detached background task, not awaited directly from
///         <see cref="HandleAsync" />, mirroring <c>ZoneNotifyEventSubscriber</c>'s/
///         <c>CaaRecordReconciliationEventSubscriber</c>'s own exact reasoning (architectural
///         invariant #3): <see cref="Ninlil.Dns.Server.Core.Events.DnsServerEventDispatcher" />
///         processes one event at a time, and a slow or unreachable webhook target must never
///         stall every other subscriber's own handling of every other event. The background task
///         opens its own dependency injection scope for the same reason those two subscribers do
///         -- the scope <see cref="HandleAsync" /> was invoked in is disposed as soon as every
///         subscriber's own <see cref="System.Threading.Tasks.ValueTask" /> for this event
///         completes, well before this detached delivery work is actually done.
///     </para>
/// </summary>
/// <typeparam name="TEvent">The webhook-eligible event type this closed generic subscriber handles.</typeparam>
/// <param name="scopeFactory">The factory used to reach a fresh, scoped <see cref="WebhookDeliveryService" /> per event.</param>
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
    ///     This method does the actual work: opening a fresh scope and delegating to
    ///     <see cref="WebhookDeliveryService" />. <see cref="WebhookDeliveryService.DeliverAsync" />
    ///     itself already catches and logs every per-subscription delivery failure internally, so
    ///     this outer try/catch only ever guards against a genuinely unexpected failure (for
    ///     example, resolving a scoped service, or the event-type registry lookup itself).
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
