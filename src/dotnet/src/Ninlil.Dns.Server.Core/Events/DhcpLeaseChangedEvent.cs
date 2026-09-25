using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This event is published when a DHCP lease is granted (confirmed or renewed), released or
///     expired. <c>Dhcpv4LeaseAllocationService</c> publishes grants and releases, and
///     <c>Dhcpv4LeaseExpirySweepService</c> expiries. In <c>Ninlil.Dhcp.Server.DnsIntegration</c>,
///     <c>DhcpLeaseDnsRegistrationEventSubscriber</c> adds or removes the A/AAAA record for the
///     lease's host name and <c>DhcpClientRegistryAutoPopulationEventSubscriber</c> updates the
///     known-client list. It lives on this shared bus, like the identity events, because the bus
///     serves the whole application; it carries only an ID, so no storage types are needed.
/// </summary>
/// <param name="OccurredAtUtc">The UTC date and time the lease's state changed.</param>
/// <param name="LeaseId">The identifier of the lease that changed.</param>
/// <param name="ChangeKind">What happened to the lease.</param>
public record DhcpLeaseChangedEvent(
    DateTimeOffset OccurredAtUtc,
    Guid LeaseId,
    DhcpLeaseChangeKindEnum ChangeKind
) : IDnsServerEvent;
