using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Forwarding;

/// <summary>
///     This class keeps a <see cref="Common.Enum.ZoneTypeEnum.Forwarder" /> zone's own synthetic
///     apex SOA/TXT content synced whenever <see cref="DnsZoneChangedEvent" /> reports that zone
///     changed -- every zone metadata write (a REST create/replace/patch, most immediately, since
///     that is the only place <see cref="Data.Abstractions.Entity.ZoneEntity.ForwarderAddresses" />
///     is ever written today) already publishes this same event, mirroring
///     <see cref="Ptr.PtrSyncEventSubscriber" />'s and <c>Ninlil.Dns.Server.Acme.Caa.CaaRecordReconciliationEventSubscriber</c>'s
///     own reasoning exactly: a single subscriber to the one event every content change already
///     funnels through is sufficient, with no second, bespoke trigger point needed.
///     <para>
///         Reconciliation runs in a detached background task, not awaited directly from
///         <see cref="HandleAsync" />, for the identical reason those two subscribers' own doc
///         comments give: this subscriber's own work involves real repository/database I/O and
///         must never block <see cref="DnsServerEventDispatcher" />'s one-event-at-a-time
///         processing of every other subscriber for every other event. The background task opens
///         its own dependency injection scope, since the scope <see cref="HandleAsync" /> was
///         invoked in is disposed as soon as every subscriber's own
///         <see cref="System.Threading.Tasks.ValueTask" /> completes, well before this detached
///         work is done.
///     </para>
/// </summary>
/// <param name="scopeFactory">
///     The factory used to reach a fresh, scoped <see cref="ForwarderZoneContentSyncService" />
///     per event.
/// </param>
/// <param name="logger">The logger this subscriber records sync failures to.</param>
public class ForwarderZoneContentSyncEventSubscriber(
    IServiceScopeFactory scopeFactory,
    ILogger<ForwarderZoneContentSyncEventSubscriber> logger
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
    ///     <see cref="ForwarderZoneContentSyncService" />, with every exception (other than
    ///     cancellation) caught and logged so one zone's own sync failure never propagates back
    ///     into <see cref="DnsServerEventDispatcher" />.
    /// </summary>
    private async Task SyncAsync(DnsZoneChangedEvent domainEvent, CancellationToken stoppingToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            ForwarderZoneContentSyncService syncService =
                scope.ServiceProvider.GetRequiredService<ForwarderZoneContentSyncService>();

            await syncService.ReconcileZoneAsync(domainEvent.ZoneId, stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Unhandled exception while syncing forwarder zone content for zone '{ZoneName}'.",
                domainEvent.ZoneName
            );
        }
    }
}
