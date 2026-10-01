using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dhcp.Server.DnsIntegration;

/// <summary>
///     This class passes each <see cref="DhcpLeaseChangedEvent" /> to
///     <see cref="DhcpDnsRegistrationService" />, keeping a client's A/AAAA record in step with its
///     lease. The work runs in a task that <see cref="HandleAsync" /> does not await, as in
///     <c>Ninlil.Dns.Server.Authoritative.Ptr.PtrSyncEventSubscriber</c>: it does database I/O and
///     must not hold up <c>DnsServerEventDispatcher</c>, which handles one event at a time. The
///     task creates its own dependency injection scope, since the dispatcher's scope is disposed
///     once <see cref="HandleAsync" /> returns. Concurrent events for leases in the same zone run
///     in parallel.
/// </summary>
/// <param name="scopeFactory">The factory that creates a scope, and a <see cref="DhcpDnsRegistrationService" />, per event.</param>
/// <param name="logger">The logger registration failures are recorded to.</param>
public class DhcpLeaseDnsRegistrationEventSubscriber(
    IServiceScopeFactory scopeFactory,
    ILogger<DhcpLeaseDnsRegistrationEventSubscriber> logger
) : IDnsServerEventSubscriber<DhcpLeaseChangedEvent>
{
    /// <inheritdoc />
    public ValueTask HandleAsync(DhcpLeaseChangedEvent domainEvent, CancellationToken stoppingToken)
    {
        _ = RegisterAsync(domainEvent, stoppingToken);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    ///     This method creates a scope and calls <see cref="DhcpDnsRegistrationService" />. Every
    ///     exception except cancellation is caught and logged, since nothing awaits this task.
    /// </summary>
    private async Task RegisterAsync(
        DhcpLeaseChangedEvent domainEvent,
        CancellationToken stoppingToken
    )
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            DhcpDnsRegistrationService registrationService =
                scope.ServiceProvider.GetRequiredService<DhcpDnsRegistrationService>();

            await registrationService.HandleLeaseChangeAsync(
                domainEvent.LeaseId,
                domainEvent.ChangeKind,
                stoppingToken
            );
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Unhandled exception while registering DNS records for DHCP lease {LeaseId}.",
                domainEvent.LeaseId
            );
        }
    }
}
