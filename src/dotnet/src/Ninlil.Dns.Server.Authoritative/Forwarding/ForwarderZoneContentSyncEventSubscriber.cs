using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Forwarding;

/// <summary>
///     This class runs <see cref="ForwarderZoneContentSyncService" /> on each
///     <see cref="DnsZoneChangedEvent" />, keeping a <see cref="Common.Enum.ZoneTypeEnum.Forwarder" />
///     zone's synthetic apex SOA and TXT records in step with
///     <see cref="Data.Abstractions.Entity.ZoneEntity.ForwarderAddresses" />. Zone writes through the
///     REST API publish that event, so no other trigger is needed.
///     <para>
///     The sync runs in a detached task, as in <see cref="Ptr.PtrSyncEventSubscriber" />, so
///     database work does not hold up <see cref="DnsServerEventDispatcher" />, which handles one
///     event at a time. It creates its DI scope because the handler's scope is disposed when
///     <see cref="HandleAsync" /> returns.
///     </para>
/// </summary>
/// <param name="scopeFactory">
///     The factory used to create a scope and <see cref="ForwarderZoneContentSyncService" /> per
///     event.
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
    ///     This method creates a scope and calls <see cref="ForwarderZoneContentSyncService" />,
    ///     logging any exception except cancellation.
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
