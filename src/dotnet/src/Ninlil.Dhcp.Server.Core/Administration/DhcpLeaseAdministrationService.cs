using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Administration;

/// <summary>
///     This class implements <see cref="IDhcpLeaseAdministrationService" /> on
///     <see cref="ILeaseRepository" />. It sets <c>State</c> and <c>LeaseExpiresAtUtc</c> as the
///     <c>ReleaseAsync</c> methods of <c>Allocation.Dhcpv4LeaseAllocationService</c> and
///     <c>Allocation.Dhcpv6LeaseAllocationService</c> do, but finds the lease by identifier, so
///     it needs no scope or request message. <c>Program.cs</c> registers it for every
///     configuration rather than inside <see cref="Extension
///     .ServiceCollectionDhcpServerCoreExtension.AddNinlilDhcpServerCore" /> or
///     <c>AddNinlilDhcpv6ServerCore</c>, which only run when the local (non-relay) engine is
///     active: a recorded lease may need releasing whichever engine runs, as with
///     <see cref="Dhcpv4LeaseExpirySweepService" />.
/// </summary>
/// <param name="leaseRepository">The repository this service reads and writes leases through.</param>
/// <param name="unitOfWork">The unit of work the change is saved through.</param>
/// <param name="eventPublisher">
///     The publisher the <see cref="DhcpLeaseChangedEvent" /> goes to (the same event a client
///     release publishes; see <see cref="IDhcpLeaseAdministrationService.ForceReleaseAsync" />).
/// </param>
public sealed class DhcpLeaseAdministrationService(
    ILeaseRepository leaseRepository,
    IUnitOfWork unitOfWork,
    IDnsServerEventPublisher eventPublisher
) : IDhcpLeaseAdministrationService
{
    /// <inheritdoc />
    public async Task<LeaseEntity> ForceReleaseAsync(
        Guid leaseId,
        CancellationToken cancellationToken = default
    )
    {
        LeaseEntity lease = await leaseRepository.GetByIdAsync(leaseId, cancellationToken);
        if (lease is null)
            return null;

        // Already released: return it unchanged rather than re-stamping the times and
        // publishing a second release event.
        if (lease.State == LeaseStateEnum.Released)
            return lease;

        DateTimeOffset now = DateTimeOffset.UtcNow;
        lease.State = LeaseStateEnum.Released;
        lease.LeaseExpiresAtUtc = now;
        lease.UpdatedAtUtc = now;

        leaseRepository.Update(lease);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        eventPublisher.Publish(
            new DhcpLeaseChangedEvent(now, lease.Id, DhcpLeaseChangeKindEnum.Released)
        );

        return lease;
    }
}
