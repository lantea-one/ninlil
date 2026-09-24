using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.CatalogSync;

/// <summary>
///     This class keeps a <c>SecondaryCatalog</c> zone's own auto-provisioned member zones synced
///     whenever <see cref="DnsZoneChangedEvent" /> reports that it changed -- every catalog zone
///     content change already funnels through <c>IZoneUpdateService</c> (an AXFR/IXFR secondary
///     refresh applying a fresh transfer is the one real trigger this ever fires for in
///     practice), which already publishes this same event, so a single subscriber to it is
///     sufficient. Also fires, harmlessly, on every other zone's own content change -- see
///     <see cref="CatalogZoneAutoProvisioningService.ReconcileZoneAsync" />'s own immediate,
///     cheap no-op for any zone that is not itself a <c>SecondaryCatalog</c>.
///     <para>
///         Reconciliation runs in a detached background task, not awaited directly from
///         <see cref="HandleAsync" />, mirroring <c>PtrSyncEventSubscriber</c>'s own reasoning
///         exactly: this subscriber's own work involves real repository/database I/O (and, for a
///         newly-discovered member, a further AXFR/IXFR pull once
///         <c>ISecondaryZoneRefreshTrigger.RequestImmediateRefresh</c> fires), so it must never
///         block <c>DnsServerEventDispatcher</c>'s one-event-at-a-time processing of every other
///         subscriber for every other event.
///     </para>
/// </summary>
/// <param name="scopeFactory">
///     The factory used to reach a fresh, scoped <see cref="CatalogZoneAutoProvisioningService" />
///     per event.
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
    ///     This method does the actual work: opening a fresh scope and delegating to
    ///     <see cref="CatalogZoneAutoProvisioningService" />, with every exception (other than
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
