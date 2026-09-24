using Microsoft.AspNetCore.Mvc;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dhcp.Server.Core.Administration;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Web.Examples.DhcpLeases;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller reports this server's own live DHCP lease state -- a lease is a live
///     artifact the allocation engine itself creates and updates as clients come and go, never an
///     operator-created resource, so this controller offers no <c>PUT</c>/<c>PATCH</c>/<c>DELETE</c>
///     at all, and its one write action (<see cref="Release" />) is an action-style endpoint that
///     force-releases an existing lease rather than an ordinary resource mutation. Every <c>GET</c>
///     requires <c>dhcpleases:read</c>; <see cref="Release" /> requires <c>dhcpleases:manage</c>.
///     <para>
///         <b>Flat, all-scopes listing, with an optional per-scope narrowing.</b> Unlike this
///         project's own scope-nested reservation/custom-option resources, a lease
///         (<see cref="LeaseEntity" />) carries its owning scope by name
///         (<see cref="LeaseEntity.ScopeName" />), not by identifier -- so rather than nesting this
///         resource under <c>/api/dhcp-scopes/{scopeId}</c> the way <see cref="DhcpReservationsController" />
///         does, <see cref="List" /> lives at the top level and accepts an optional
///         <c>scopeId</c> query parameter, resolved to that scope's own current name before being
///         pushed down into <see cref="ILeaseRepository.SearchByScopeNameAsync" />. This also
///         suits how an operator actually wants to see this data: a flat, all-scopes lease table
///         is the natural "what is my network doing right now" view, with narrowing to one scope
///         as a secondary refinement rather than the primary access path.
///     </para>
/// </summary>
/// <param name="leaseRepository">The repository this controller reads leases through.</param>
/// <param name="scopeRepository">The repository used to resolve an optional <c>scopeId</c> filter into a scope name.</param>
/// <param name="leaseAdministrationService">
///     The service <see cref="Release" /> delegates a force-release to -- see that service's own
///     doc comment for why this is the same mutation, and the same downstream
///     <c>DhcpLeaseChangedEvent</c>, a real client-initiated release already produces.
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
    ///     This action lists every DHCP lease this server has recorded, optionally narrowed to one
    ///     scope via <paramref name="scopeId" />.
    /// </summary>
    [HttpGet]
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

        IReadOnlyList<LeaseEntity> scopedLeases = await leaseRepository.SearchByScopeNameAsync(
            scope.Name,
            skip,
            take,
            stoppingToken
        );
        int scopedTotalCount = await leaseRepository.CountByScopeNameAsync(
            scope.Name,
            stoppingToken
        );

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
    ///     This action looks up a single lease by its identifier.
    /// </summary>
    [HttpGet("{id:guid}")]
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
    ///     This action force-releases a lease by its own identifier, letting an operator reclaim a
    ///     stuck or stale address without waiting for the client itself to send a real DHCP RELEASE
    ///     or for this server's own natural expiry sweep to eventually catch it. Delegates directly
    ///     to <see cref="IDhcpLeaseAdministrationService.ForceReleaseAsync" /> -- see that method's
    ///     own doc comment for exactly what it does (the identical mutation, and the identical
    ///     downstream <c>DhcpLeaseChangedEvent</c>, a real client-initiated release already
    ///     produces, so the address is genuinely available for reallocation immediately, and any
    ///     DNS record this lease auto-registered is cleaned up exactly as it would be for a real
    ///     release). Idempotent: releasing an already-released lease is a safe no-op that still
    ///     returns <c>200 OK</c>.
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
    ///     This method converts a lease entity into its REST representation.
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
