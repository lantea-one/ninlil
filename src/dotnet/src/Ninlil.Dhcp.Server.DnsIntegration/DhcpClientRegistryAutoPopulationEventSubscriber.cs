using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dhcp.Server.DnsIntegration;

/// <summary>
///     This class passes each <see cref="DhcpLeaseChangedEvent" /> to
///     <see cref="DhcpClientRegistryAutoPopulationService" />. It subscribes to the same event as
///     <see cref="DhcpLeaseDnsRegistrationEventSubscriber" />; <c>DnsServerEventDispatcher</c> calls
///     every registered subscriber for an event type. It is a separate subscriber because the
///     reasons <see cref="DhcpDnsRegistrationService" /> skips a lease (no DNS zone, an ineligible
///     zone) do not apply to the registry.
///     <para>
///         As in <see cref="DhcpLeaseDnsRegistrationEventSubscriber" />, the work runs in a task
///         that is not awaited, with its own dependency injection scope, so database I/O does not
///         hold up the dispatcher.
///     </para>
/// </summary>
/// <param name="scopeFactory">The factory that creates a scope, and a <see cref="DhcpClientRegistryAutoPopulationService" />, per event.</param>
/// <param name="logger">The logger failures are recorded to.</param>
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
    ///     This method creates a scope and calls <see cref="DhcpClientRegistryAutoPopulationService" />.
    ///     Every exception except cancellation is caught and logged, since nothing awaits this task.
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
