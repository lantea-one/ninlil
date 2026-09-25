using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.CatalogSync;

/// <summary>
///     This class runs <see cref="CatalogZoneAutoProvisioningService" /> on each
///     <see cref="DnsZoneChangedEvent" />, so the member zones of a <c>SecondaryCatalog</c> zone
///     follow its content. In practice the trigger is a transfer pull of the catalog zone through
///     <c>IZoneUpdateService</c>. For other zones the service returns at once.
///     <para>
///     The work runs in a detached task, as in <c>PtrSyncEventSubscriber</c>, because it does
///     database work (and triggers pulls of new members through
///     <c>ISecondaryZoneRefreshTrigger.RequestImmediateRefresh</c>) and must not hold up
///     <c>DnsServerEventDispatcher</c>, which handles one event at a time.
///     </para>
/// </summary>
/// <param name="scopeFactory">
///     The factory used to create a scope and <see cref="CatalogZoneAutoProvisioningService" /> per
///     event.
/// </param>
/// <param name="logger">The logger this subscriber records reconciliation failures to.</param>
public class CatalogZoneAutoProvisioningEventSubscriber(
    IServiceScopeFactory scopeFactory,
    ILogger<CatalogZoneAutoProvisioningEventSubscriber> logger
) : IDnsServerEventSubscriber<DnsZoneChangedEvent>
{
    /// <inheritdoc />
    public ValueTask HandleAsync(DnsZoneChangedEvent domainEvent, CancellationToken stoppingToken)
    {
        _ = ReconcileAsync(domainEvent, stoppingToken);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    ///     This method creates a scope and calls <see cref="CatalogZoneAutoProvisioningService" />,
    ///     logging any exception except cancellation.
    /// </summary>
    private async Task ReconcileAsync(
        DnsZoneChangedEvent domainEvent,
        CancellationToken stoppingToken
    )
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            CatalogZoneAutoProvisioningService service =
                scope.ServiceProvider.GetRequiredService<CatalogZoneAutoProvisioningService>();

            await service.ReconcileZoneAsync(domainEvent.ZoneId, stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Unhandled exception while reconciling catalog zone membership for zone '{ZoneName}'.",
                domainEvent.ZoneName
            );
        }
    }
}
