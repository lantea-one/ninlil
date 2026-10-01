using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.Acme.Caa;

/// <summary>
///     This class reconciles a zone's CAA records whenever <see cref="DnsZoneChangedEvent" />
///     reports a change: above all when the zone's <c>AutoManageCaaRecords</c> or
///     <c>CaaIssuerDomain</c> is changed through <c>ZonesController</c>, which raises the event,
///     but also on every other change to the zone. That is cheap, because a pass that finds the
///     records already correct writes nothing (and so raises no new event).
///     <para>
///         <see cref="HandleAsync" /> starts the work on a detached task, like
///         <c>ZoneNotifyEventSubscriber</c>: it does database I/O and must not hold up
///         <c>DnsServerEventDispatcher</c>, which handles one event at a time. The task opens its
///         own dependency injection scope, because the dispatcher's scope is disposed once every
///         subscriber has returned.
///     </para>
/// </summary>
/// <param name="scopeFactory">The factory used to create a scope, and a <see cref="CaaRecordReconciliationService" />, per event.</param>
/// <param name="logger">The logger this subscriber records reconciliation failures to.</param>
public class CaaRecordReconciliationEventSubscriber(
    IServiceScopeFactory scopeFactory,
    ILogger<CaaRecordReconciliationEventSubscriber> logger
) : IDnsServerEventSubscriber<DnsZoneChangedEvent>
{
    /// <inheritdoc />
    public ValueTask HandleAsync(DnsZoneChangedEvent domainEvent, CancellationToken stoppingToken)
    {
        _ = ReconcileAsync(domainEvent, stoppingToken);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    ///     This method opens a scope and calls <see cref="CaaRecordReconciliationService" />,
    ///     logging any exception other than cancellation, since nothing awaits this task.
    /// </summary>
    private async Task ReconcileAsync(
        DnsZoneChangedEvent domainEvent,
        CancellationToken stoppingToken
    )
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            CaaRecordReconciliationService reconciliationService =
                scope.ServiceProvider.GetRequiredService<CaaRecordReconciliationService>();

            await reconciliationService.ReconcileZoneAsync(domainEvent.ZoneId, stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Unhandled exception while reconciling CAA records for zone '{ZoneName}'.",
                domainEvent.ZoneName
            );
        }
    }
}
