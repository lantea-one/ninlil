using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Ptr;

/// <summary>
///     This class keeps a forward zone's implied PTR records synced whenever
///     <see cref="DnsZoneChangedEvent" /> reports that it changed -- every zone content change
///     already funnels through <c>IZoneUpdateService</c> (a REST create/update/delete, an RFC
///     2136 dynamic update, an AXFR/IXFR secondary refresh, or a cloud sync pull), which already
///     publishes this same event on every one of those paths, so a single subscriber to it is
///     sufficient to cover "an A/AAAA record was created, updated, or deleted" -- no second,
///     bespoke trigger point was needed the way CAA reconciliation needed one for
///     "ACME enrollment/CA selection changed" (nothing analogous to that exists for PTR sync --
///     everything relevant here is already a zone content change). Also fires, harmlessly, on
///     every other zone content change (a CNAME edit, a CAA reconciliation write, and even this
///     feature's own PTR writes to the reverse zone) since <see cref="PtrSyncService" />'s own
///     reconciliation is a cheap, idempotent no-op once a zone's implied PTR record set already
///     matches its desired state.
///     <para>
///         Reconciliation runs in a detached background task, not awaited directly from
///         <see cref="HandleAsync" />, mirroring
///         <c>Ninlil.Dns.Server.Acme.Caa.CaaRecordReconciliationEventSubscriber</c>'s own
///         reasoning exactly: this subscriber's own work involves real repository/database I/O,
///         so it must never block <c>DnsServerEventDispatcher</c>'s one-event-at-a-time
///         processing of every other subscriber for every other event. The background task opens
///         its own dependency injection scope for the same reason -- the scope
///         <see cref="HandleAsync" /> was invoked in is disposed as soon as every subscriber's
///         own <see cref="System.Threading.Tasks.ValueTask" /> completes, well before this
///         detached work is done.
///     </para>
/// </summary>
/// <param name="scopeFactory">The factory used to reach a fresh, scoped <see cref="PtrSyncService" /> per event.</param>
/// <param name="logger">The logger this subscriber records sync failures to.</param>
public class PtrSyncEventSubscriber(
    IServiceScopeFactory scopeFactory,
    ILogger<PtrSyncEventSubscriber> logger
) : IDnsServerEventSubscriber<DnsZoneChangedEvent>
{
    /// <inheritdoc />
    public ValueTask HandleAsync(DnsZoneChangedEvent domainEvent, CancellationToken stoppingToken)
    {
        _ = SyncAsync(domainEvent, stoppingToken);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    ///     This method does the actual work: opening a fresh scope and delegating to
    ///     <see cref="PtrSyncService" />, with every exception (other than cancellation) caught
    ///     and logged so one zone's own sync failure never propagates back into
    ///     <c>DnsServerEventDispatcher</c>.
    /// </summary>
    private async Task SyncAsync(DnsZoneChangedEvent domainEvent, CancellationToken stoppingToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            PtrSyncService ptrSyncService =
                scope.ServiceProvider.GetRequiredService<PtrSyncService>();

            await ptrSyncService.ReconcileZoneAsync(domainEvent.ZoneId, stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Unhandled exception while syncing PTR records for zone '{ZoneName}'.",
                domainEvent.ZoneName
            );
        }
    }
}
