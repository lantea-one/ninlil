using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Ptr;

/// <summary>
///     This class runs PTR sync (<see cref="PtrSyncService" />) for a zone on each
///     <see cref="DnsZoneChangedEvent" />. Every record change (REST, dynamic update, zone transfer,
///     cloud sync) goes through <c>IZoneUpdateService</c>, which publishes the event, so this one
///     trigger covers every A/AAAA change. It also runs on unrelated changes, including its PTR
///     writes to reverse zones; the sync makes no change when records already match.
///     <para>
///     The sync runs in a detached task instead of being awaited in
///     <see cref="HandleAsync" />, as in <c>CaaRecordReconciliationEventSubscriber</c>: it does
///     database work and must not hold up <c>DnsServerEventDispatcher</c>, which handles one event
///     at a time. It creates its DI scope because the handler's scope is disposed when
///     <see cref="HandleAsync" /> returns.
///     </para>
/// </summary>
/// <param name="scopeFactory">The factory used to create a scope and <see cref="PtrSyncService" /> per event.</param>
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
    ///     This method creates a scope and calls <see cref="PtrSyncService" />, logging any exception
    ///     except cancellation.
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
