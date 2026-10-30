using Ninlil.Dhcp.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Administration;

/// <summary>
///     This interface lets an operator force-release a stuck or stale <see cref="LeaseEntity" />
///     from the REST API and UI, without waiting for the client's DHCPv4 RELEASE (RFC 2131
///     Section 4.3.4) or DHCPv6 RELEASE (RFC 8415 Section 18.2.7) or for
///     <see cref="Dhcpv4LeaseExpirySweepService" />. Apart from the expiry sweep, it is the only
///     change to a lease that does not come from a client message.
/// </summary>
public interface IDhcpLeaseAdministrationService
{
    /// <summary>
    ///     This method force-releases a lease by identifier, whatever its state, so its address
    ///     is free again at once. The result is the same as a client RELEASE: the lease is
    ///     <see cref="Ninlil.Dns.Common.Enum.LeaseStateEnum.Released" />, which allocation treats
    ///     as available (see <c>Allocation.Dhcpv4LeaseAllocationService.IsLeaseBlocking</c>), and
    ///     the same <see cref="Ninlil.Dns.Server.Core.Events.DhcpLeaseChangedEvent" />
    ///     (<see cref="Ninlil.Dns.Common.Enum.DhcpLeaseChangeKindEnum.Released" />) is published, so
    ///     subscribers such as
    ///     <c>Ninlil.Dhcp.Server.DnsIntegration.DhcpLeaseDnsRegistrationEventSubscriber</c> (which
    ///     removes the lease's A/AAAA record) need no special case.
    /// </summary>
    /// <param name="leaseId">The identifier of the lease to force-release.</param>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     The released lease, or <see langword="null" /> if there is no lease with that
    ///     identifier. A lease that is already released is returned unchanged and no event is
    ///     published.
    /// </returns>
    Task<LeaseEntity> ForceReleaseAsync(
        Guid leaseId,
        CancellationToken cancellationToken = default
    );
}
