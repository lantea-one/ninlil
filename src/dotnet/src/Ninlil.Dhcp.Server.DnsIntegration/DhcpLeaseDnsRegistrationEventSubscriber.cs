using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dhcp.Server.DnsIntegration;

/// <summary>
///     This class bridges <see cref="DhcpLeaseChangedEvent" /> to <see cref="DhcpDnsRegistrationService" />
///     -- the one subscriber that keeps a DHCP client's own A/AAAA registration in sync with its
///     lease's real lifecycle. Reconciliation runs in a detached background task, not awaited
///     directly from <see cref="HandleAsync" />, mirroring
///     <c>Ninlil.Dns.Server.Authoritative.Ptr.PtrSyncEventSubscriber</c>'s own reasoning exactly:
///     this subscriber's own work involves real repository/database I/O, so it must never block
///     <c>DnsServerEventDispatcher</c>'s one-event-at-a-time processing of every other subscriber
///     for every other event. The background task opens its own dependency injection scope for
///     the same reason -- the scope <see cref="HandleAsync" /> was invoked in is disposed as soon
///     as every subscriber's own <see cref="System.Threading.Tasks.ValueTask" /> completes, well
///     before this detached work is done.
/// </summary>
/// <param name="scopeFactory">The factory used to reach a fresh, scoped <see cref="DhcpDnsRegistrationService" /> per event.</param>
/// <param name="logger">The logger this subscriber records registration failures to.</param>
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
    ///     This method does the actual work: opening a fresh scope and delegating to
    ///     <see cref="DhcpDnsRegistrationService" />, with every exception (other than
    ///     cancellation) caught and logged so one lease's own registration failure never
    ///     propagates back into <c>DnsServerEventDispatcher</c>.
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
