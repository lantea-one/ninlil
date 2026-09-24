using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Administration;

/// <summary>
///     This class implements <see cref="IDhcpLeaseAdministrationService" /> against the real
///     <see cref="ILeaseRepository" />, mutating a lease's own <c>State</c>/<c>LeaseExpiresAtUtc</c>
///     the identical way <c>Allocation.Dhcpv4LeaseAllocationService.ReleaseAsync</c>/
///     <c>Allocation.Dhcpv6LeaseAllocationService.ReleaseAsync</c> already do for a real
///     client-initiated release -- this class is simply a third entry point into that same
///     mutation, reached by lease identifier directly rather than by decoding a real client RELEASE
///     message, so it needs neither a scope nor a protocol-specific request message the way those
///     two methods do. Deliberately registered unconditionally (see this project's own composition
///     root, <c>Ninlil.Web.Program</c>) rather than folded into <see cref="Extension
///     .ServiceCollectionDhcpServerCoreExtension.AddNinlilDhcpServerCore" />/
///     <c>AddNinlilDhcpv6ServerCore</c>: those two methods are only ever called for whichever
///     protocol's local (non-relay) engine is active, but a lease already on record can still need
///     force-releasing regardless of which engine variant currently grants new ones -- the same
///     reasoning already documented on <see cref="Dhcpv4LeaseExpirySweepService" />.
/// </summary>
/// <param name="leaseRepository">The repository this service reads and writes leases through.</param>
/// <param name="unitOfWork">The unit of work this service commits its change through.</param>
/// <param name="eventPublisher">
///     The publisher a genuine force-release reports through, via
///     <see cref="DhcpLeaseChangedEvent" /> -- see this interface's own doc comment for why this
///     is the exact same event a real client-initiated release already publishes.
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

        // Already released -- a safe, idempotent no-op, per this interface's own doc comment,
        // rather than re-stamping LeaseExpiresAtUtc/UpdatedAtUtc and publishing a second,
        // misleading "just released" event for a lease that was really released earlier.
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
