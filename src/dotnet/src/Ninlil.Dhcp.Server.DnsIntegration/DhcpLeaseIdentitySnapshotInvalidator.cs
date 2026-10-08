using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dhcp.Server.DnsIntegration;

/// <summary>
///     This class refreshes the in-memory lease snapshot of
///     <see cref="DhcpLeaseClientIdentityResolver" /> whenever a lease is created, renewed,
///     released or expired. It subscribes to the same <see cref="DhcpLeaseChangedEvent" /> as
///     <see cref="DhcpLeaseDnsRegistrationEventSubscriber" /> and
///     <see cref="DhcpClientRegistryAutoPopulationEventSubscriber" />. It only starts a
///     background refresh and never waits on the database, so it does not hold up the dispatcher.
///     The event is local to this process in broker mode, so the resolver's time-to-live covers
///     changes made on another node.
/// </summary>
/// <param name="resolver">The resolver whose snapshot is refreshed.</param>
public sealed class DhcpLeaseIdentitySnapshotInvalidator(DhcpLeaseClientIdentityResolver resolver)
    : IDnsServerEventSubscriber<DhcpLeaseChangedEvent>
{
    /// <inheritdoc />
    public ValueTask HandleAsync(DhcpLeaseChangedEvent domainEvent, CancellationToken stoppingToken)
    {
        resolver.Invalidate();
        return ValueTask.CompletedTask;
    }
}
