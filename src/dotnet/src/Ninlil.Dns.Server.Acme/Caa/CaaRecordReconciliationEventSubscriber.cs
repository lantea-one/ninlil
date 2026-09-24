using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.Acme.Caa;

/// <summary>
///     This class keeps a zone's CAA record set reconciled whenever
///     <see cref="DnsZoneChangedEvent" /> reports that it changed -- most importantly, when a
///     zone's own <c>AutoManageCaaRecords</c>/<c>CaaIssuerDomain</c> fields themselves are
///     changed via the REST API's <c>ZonesController</c> (which already fires this same event on
///     every zone metadata write), but also, harmlessly, on every other zone content change,
///     since <see cref="CaaRecordReconciliationService" />'s own reconciliation is a cheap,
///     idempotent no-op once a zone's CAA record set already matches its desired state.
///     <para>
///         Reconciliation runs in a detached background task, not awaited directly from
///         <see cref="HandleAsync" />, mirroring
///         <c>Ninlil.Dns.Server.Authoritative.Notify.ZoneNotifyEventSubscriber</c>'s own
///         reasoning exactly: this subscriber's own work involves real repository/database I/O
///         (unlike <c>SignedZoneChangedEventSubscriber</c>'s purely-local in-memory re-sign), so
///         it must never block <c>DnsServerEventDispatcher</c>'s one-event-at-a-time processing of
///         every other subscriber for every other event. The background task opens its own
///         dependency injection scope for the same reason -- the scope <see cref="HandleAsync" />
///         was invoked in is disposed as soon as every subscriber's own
///         <see cref="System.Threading.Tasks.ValueTask" /> completes, well before this detached
///         work is done.
///     </para>
/// </summary>
/// <param name="scopeFactory">The factory used to reach a fresh, scoped <see cref="CaaRecordReconciliationService" /> per event.</param>
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
    ///     This method does the actual work: opening a fresh scope and delegating to
    ///     <see cref="CaaRecordReconciliationService" />, with every exception (other than
    ///     cancellation) caught and logged so one zone's own reconciliation failure never
    ///     propagates back into <c>DnsServerEventDispatcher</c>.
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
