using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dhcp.Server.DnsIntegration;

/// <summary>
///     This class bridges <see cref="DhcpLeaseChangedEvent" /> to
///     <see cref="DhcpClientRegistryAutoPopulationService" /> -- a sibling subscriber to
///     <see cref="DhcpLeaseDnsRegistrationEventSubscriber" />, registered against the exact same
///     event, since <c>DnsServerEventDispatcher</c> already invokes every registered
///     <c>IDnsServerEventSubscriber&lt;TEvent&gt;</c> for a given event type (see its own
///     <c>scope.ServiceProvider.GetServices(subscriberInterface)</c> loop) rather than just the
///     first one found. A separate subscriber, rather than folding this into the existing one, is
///     deliberate: <see cref="DhcpDnsRegistrationService" />'s own early-return skip paths (no DNS
///     zone designated, an ineligible zone type, and so on) have nothing to do with whether a
///     known-client row should exist -- that decision only ever depends on the lease itself, never
///     on the owning scope's own DHCP-&gt;DNS integration configuration.
///     <para>
///         Reconciliation runs in a detached background task, not awaited directly from
///         <see cref="HandleAsync" />, mirroring <see cref="DhcpLeaseDnsRegistrationEventSubscriber" />'s
///         own reasoning exactly: this subscriber's own work involves real repository/database I/O,
///         so it must never block <c>DnsServerEventDispatcher</c>'s one-event-at-a-time processing
///         of every other subscriber for every other event. The background task opens its own
///         dependency injection scope for the same reason.
///     </para>
/// </summary>
/// <param name="scopeFactory">The factory used to reach a fresh, scoped <see cref="DhcpClientRegistryAutoPopulationService" /> per event.</param>
/// <param name="logger">The logger this subscriber records failures to.</param>
public class DhcpClientRegistryAutoPopulationEventSubscriber(
    IServiceScopeFactory scopeFactory,
    ILogger<DhcpClientRegistryAutoPopulationEventSubscriber> logger
) : IDnsServerEventSubscriber<DhcpLeaseChangedEvent>
{
    /// <inheritdoc />
    public ValueTask HandleAsync(DhcpLeaseChangedEvent domainEvent, CancellationToken stoppingToken)
    {
        _ = AutoPopulateAsync(domainEvent, stoppingToken);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    ///     This method does the actual work: opening a fresh scope and delegating to
    ///     <see cref="DhcpClientRegistryAutoPopulationService" />, with every exception (other
    ///     than cancellation) caught and logged so one lease's own auto-population failure never
    ///     propagates back into <c>DnsServerEventDispatcher</c>.
    /// </summary>
    private async Task AutoPopulateAsync(
        DhcpLeaseChangedEvent domainEvent,
        CancellationToken stoppingToken
    )
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            DhcpClientRegistryAutoPopulationService autoPopulationService =
                scope.ServiceProvider.GetRequiredService<DhcpClientRegistryAutoPopulationService>();

            await autoPopulationService.HandleLeaseChangeAsync(
                domainEvent.LeaseId,
                domainEvent.ChangeKind,
                stoppingToken
            );
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Unhandled exception while auto-populating the known-client registry for DHCP lease {LeaseId}.",
                domainEvent.LeaseId
            );
        }
    }
}
