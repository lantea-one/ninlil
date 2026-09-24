using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This event is published whenever a DHCP lease's own lifecycle state changes in a way a
///     subscriber might care about -- granted (confirmed or renewed), released, or expired.
///     Published by <c>Ninlil.Dhcp.Server.Core.Allocation.Dhcpv4LeaseAllocationService</c> (grant/
///     release) and <c>Ninlil.Dhcp.Server.Core.Dhcpv4LeaseExpirySweepService</c> (expiry); its own
///     one real subscriber today is the DHCP-&gt;DNS integration feature
///     (<c>Ninlil.Dhcp.Server.DnsIntegration.DhcpLeaseDnsRegistrationEventSubscriber</c>),
///     registering/removing an A/AAAA record for the lease's own presented host name. Deliberately
///     defined here, in the shared, protocol-agnostic event bus namespace every other
///     cross-cutting event in this project already uses -- not in a DHCP-specific project -- the
///     same reasoning <see cref="UserAccountChangedEvent" />/<see cref="RoleChangedEvent" />
///     already establish for identity/RBAC events that are not DNS-specific either: this bus is
///     this whole application's own shared cross-cutting-concern channel, not a DNS-only one, and
///     <see cref="Ninlil.Dns.Server.Core" /> already has no dependency on any storage-layer entity
///     type at all (an id-only event, mirroring <see cref="DnsFilterListChangedEvent" />'s own
///     shape, needs none).
/// </summary>
/// <param name="OccurredAtUtc">The UTC date and time the lease's state actually changed.</param>
/// <param name="LeaseId">The identifier of the lease that changed.</param>
/// <param name="ChangeKind">What actually happened to the lease.</param>
public record DhcpLeaseChangedEvent(
    DateTimeOffset OccurredAtUtc,
    Guid LeaseId,
    DhcpLeaseChangeKindEnum ChangeKind
) : IDnsServerEvent;
