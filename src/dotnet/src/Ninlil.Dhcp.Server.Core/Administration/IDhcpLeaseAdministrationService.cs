using Ninlil.Dhcp.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Administration;

/// <summary>
///     This interface is the one operator-facing entry point that ever mutates a
///     <see cref="LeaseEntity" /> outside a client's own real wire exchange with this server --
///     giving an operator a way to force-release a stuck or stale lease from the REST API/Angular
///     UI, without waiting for the client itself to send a real DHCPv4 RELEASE (RFC 2131 Section
///     4.3.6) or DHCPv6 RELEASE (RFC 8415 Section 18.2.7), and without needing to wait out this
///     server's own natural expiry sweep (<see cref="Dhcpv4LeaseExpirySweepService" />) either.
/// </summary>
public interface IDhcpLeaseAdministrationService
{
    /// <summary>
    ///     This method force-releases a lease by its own identifier, immediately returning its
    ///     address to the owning scope's free-address pool -- functionally equivalent to a real
    ///     client-initiated RELEASE, and also equivalent to a force-expire for a caller's purposes,
    ///     since both leave the lease in the identical <see cref="Ninlil.Dns.Common.Enum.LeaseStateEnum.Released" />
    ///     state a subsequent allocation attempt already treats as immediately available (see
    ///     <c>Allocation.Dhcpv4LeaseAllocationService.IsLeaseBlocking</c>). Publishes the exact
    ///     same <see cref="Ninlil.Dns.Server.Core.Events.DhcpLeaseChangedEvent" />
    ///     (<see cref="Ninlil.Dns.Common.Enum.DhcpLeaseChangeKindEnum.Released" />) a real
    ///     client-initiated release already publishes, so every existing subscriber -- most
    ///     notably <c>Ninlil.Dhcp.Server.DnsIntegration.DhcpLeaseDnsRegistrationEventSubscriber</c>,
    ///     which removes this lease's own auto-registered A/AAAA record -- reacts identically,
    ///     with no special-casing anywhere downstream for "this release was operator-forced rather
    ///     than client-initiated."
    /// </summary>
    /// <param name="leaseId">The identifier of the lease to force-release.</param>
    /// <param name="cancellationToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     The now-released lease, or <see langword="null" /> if no lease with that identifier
    ///     exists. Calling this against a lease that is already
    ///     <see cref="Ninlil.Dns.Common.Enum.LeaseStateEnum.Released" /> is a safe, idempotent
    ///     no-op -- the lease is returned unchanged, and no duplicate event is published.
    /// </returns>
    Task<LeaseEntity> ForceReleaseAsync(
        Guid leaseId,
        CancellationToken cancellationToken = default
    );
}
