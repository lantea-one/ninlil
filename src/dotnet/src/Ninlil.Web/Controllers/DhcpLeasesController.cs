using Microsoft.AspNetCore.Mvc;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dhcp.Server.Core.Administration;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Web.Examples.DhcpLeases;
using Ninlil.Web.Models;
using Ninlil.Web.Routing;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller reports DHCP leases. The allocation engine creates and updates leases as
///     clients come and go, so there is no PUT, PATCH or DELETE; the one write,
///     <see cref="Release" />, force-releases a lease. Reads need <c>dhcpleases:read</c> and
///     <see cref="Release" /> needs <c>dhcpleases:manage</c>.
///     <para>
///         <b>One list for all scopes, optionally narrowed.</b> A lease
///         (<see cref="LeaseEntity" />) refers to its scope by id
///         (<see cref="LeaseEntity.ScopeId" />) and an all-scopes lease table is the usual "what
///         is my network doing" view, so instead of nesting under
///         <c>/api/dhcp-scopes/{scopeId}</c> like <see cref="DhcpReservationsController" />,
///         <see cref="List" /> is top-level and takes an optional <c>scopeId</c> for
///         <see cref="ILeaseRepository.SearchByScopeIdAsync" />. The <c>scopeName</c> a lease
///         reports is its display copy of the scope's name.
///     </para>
/// </summary>
/// <param name="leaseRepository">Reads leases.</param>
/// <param name="scopeRepository">Checks that an optional <c>scopeId</c> filter names a scope.</param>
/// <param name="leaseAdministrationService">
///     Performs the force-release for <see cref="Release" />, with the same change and the same
///     <c>DhcpLeaseChangedEvent</c> as a release sent by the client.
/// </param>
[ApiController]
[Route("api/dhcp-leases")]
public class DhcpLeasesController(
    ILeaseRepository leaseRepository,
    IDhcpScopeRepository scopeRepository,
    IDhcpLeaseAdministrationService leaseAdministrationService
) : ControllerBase
{
    /// <summary>
    ///     This action lists DHCP leases, one page at a time, for every scope or only the one named
    ///     by <paramref name="scopeId" />. Returns 404 for an unknown scope. Requires
    ///     <c>dhcpleases:read</c>.
    /// </summary>
    [HttpGet]
    [ReadFromReplica]
    [RequirePermission("dhcpleases:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(DhcpLeaseDetailPagedResultExample))]
    public async Task<ActionResult<PagedResult<DhcpLeaseDetail>>> List(
        [FromQuery] Guid? scopeId,
        [FromQuery] PaginationQuery pagination,
        CancellationToken stoppingToken
    )
    {
        (int skip, int take) = (pagination ?? new PaginationQuery()).Normalize();

        if (scopeId is null)
        {
            IReadOnlyList<LeaseEntity> allLeases = await leaseRepository.SearchAsync(
                skip,
                take,
                stoppingToken
            );
            int allTotalCount = await leaseRepository.CountAsync(stoppingToken);

            return Ok(
                new PagedResult<DhcpLeaseDetail>(
                    [.. allLeases.Select(ToDetail)],
                    skip,
                    take,
                    allTotalCount
                )
            );
        }

        DhcpScopeEntity scope = await scopeRepository.GetByIdAsync(scopeId.Value, stoppingToken);
        if (scope is null)
            throw new NotFoundException("DhcpScope", scopeId.Value.ToString());

        IReadOnlyList<LeaseEntity> scopedLeases = await leaseRepository.SearchByScopeIdAsync(
            scope.Id,
            skip,
            take,
            stoppingToken
        );
        int scopedTotalCount = await leaseRepository.CountByScopeIdAsync(scope.Id, stoppingToken);

        return Ok(
            new PagedResult<DhcpLeaseDetail>(
                [.. scopedLeases.Select(ToDetail)],
                skip,
                take,
                scopedTotalCount
            )
        );
    }

    /// <summary>
    ///     This action returns one lease. Returns 404 for an unknown id. Requires
    ///     <c>dhcpleases:read</c>.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ReadFromReplica]
    [RequirePermission("dhcpleases:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(DhcpLeaseDetailExample))]
    public async Task<ActionResult<DhcpLeaseDetail>> Get(Guid id, CancellationToken stoppingToken)
    {
        LeaseEntity lease = await leaseRepository.GetByIdAsync(id, stoppingToken);
        if (lease is null)
            throw new NotFoundException("DhcpLease", id.ToString());

        return Ok(ToDetail(lease));
    }

    /// <summary>
    ///     This action force-releases a lease so an operator can reclaim a stuck or stale address
    ///     without waiting for the client's RELEASE or the expiry sweep. It makes the same
    ///     change and raises the same lease-changed event as a client release, so the
    ///     address can be handed out again at once and any DNS record the lease registered is
    ///     removed. Releasing a lease that is already released succeeds and changes nothing.
    ///     Returns 404 for an unknown id. Requires <c>dhcpleases:manage</c>.
    /// </summary>
    [HttpPost("{id:guid}/release")]
    [RequirePermission("dhcpleases:manage")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(DhcpLeaseDetailExample))]
    public async Task<ActionResult<DhcpLeaseDetail>> Release(
        Guid id,
        CancellationToken stoppingToken
    )
    {
        LeaseEntity lease = await leaseAdministrationService.ForceReleaseAsync(id, stoppingToken);
        if (lease is null)
            throw new NotFoundException("DhcpLease", id.ToString());

        return Ok(ToDetail(lease));
    }

    /// <summary>
    ///     This method converts a lease entity to its API representation.
    /// </summary>
    private static DhcpLeaseDetail ToDetail(LeaseEntity lease) =>
        new(
            lease.Id,
            lease.ProtocolVersion,
            lease.ScopeName,
            lease.ClientIdentifier,
            lease.IpAddress,
            lease.HostName,
            lease.State,
            lease.LeaseObtainedAtUtc,
            lease.LeaseExpiresAtUtc,
            lease.CreatedAtUtc,
            lease.UpdatedAtUtc,
            lease.PrefixLength
        );
}
