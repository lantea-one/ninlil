using System.Net;
using Microsoft.AspNetCore.Mvc;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Enum;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Web.Examples.DhcpReservations;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller administers the static reservations belonging to a single DHCP scope,
///     nested under it (<c>/api/dhcp-scopes/{scopeId}/reservations</c>) since a reservation only
///     ever makes sense in its owning scope's own context -- the same nesting
///     <see cref="RecordsController" /> already establishes for a zone's records. Every action
///     requires the <c>dhcpscopes:read</c> or <c>dhcpscopes:write</c> permission, the same as
///     <see cref="DhcpScopesController" />.
///     <para>
///         <b>Address-assignment scopes only.</b> RFC 8415 defines no delegated-prefix
///         reservation shape at all -- a reservation is always a fixed ordinary address, never a
///         fixed delegated prefix -- so <see cref="Create" />/<see cref="Replace" /> both reject a
///         write against a <see cref="DhcpScopeKindEnum.PrefixDelegation" /> scope with a real
///         <see cref="ValidationException" />, rather than silently accepting a reservation shape
///         this feature has no way to actually honor.
///     </para>
/// </summary>
/// <param name="scopeRepository">The repository used to confirm the owning scope exists, and to check its own <see cref="DhcpScopeEntity.Kind" />.</param>
/// <param name="reservationRepository">The repository this controller reads and writes reservations through directly.</param>
/// <param name="unitOfWork">The unit of work every staged change is committed through.</param>
[ApiController]
[Route("api/dhcp-scopes/{scopeId:guid}/reservations")]
public class DhcpReservationsController(
    IDhcpScopeRepository scopeRepository,
    IDhcpReservationRepository reservationRepository,
    IUnitOfWork unitOfWork
) : ControllerBase
{
    /// <summary>
    ///     This action lists every reservation belonging to a DHCP scope.
    /// </summary>
    [HttpGet]
    [RequirePermission("dhcpscopes:read")]
    [SwaggerResponseExample(
        StatusCodes.Status200OK,
        typeof(DhcpReservationDetailPagedResultExample)
    )]
    public async Task<ActionResult<PagedResult<DhcpReservationDetail>>> List(
        Guid scopeId,
        [FromQuery] PaginationQuery pagination,
        CancellationToken stoppingToken
    )
    {
        if (await scopeRepository.GetByIdAsync(scopeId, stoppingToken) is null)
            throw new NotFoundException("DhcpScope", scopeId.ToString());

        (int skip, int take) = (pagination ?? new PaginationQuery()).Normalize();

        IReadOnlyList<DhcpReservationEntity> reservations =
            await reservationRepository.SearchByScopeIdAsync(scopeId, skip, take, stoppingToken);
        int totalCount = await reservationRepository.CountByScopeIdAsync(scopeId, stoppingToken);

        return Ok(
            new PagedResult<DhcpReservationDetail>(
                [.. reservations.Select(ToDetail)],
                skip,
                take,
                totalCount
            )
        );
    }

    /// <summary>
    ///     This action looks up a single reservation by its identifier.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("dhcpscopes:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(DhcpReservationDetailExample))]
    public async Task<ActionResult<DhcpReservationDetail>> Get(
        Guid scopeId,
        Guid id,
        CancellationToken stoppingToken
    )
    {
        DhcpReservationEntity reservation = await reservationRepository.GetByIdAsync(
            id,
            stoppingToken
        );
        if (reservation is null || reservation.ScopeId != scopeId)
            throw new NotFoundException("DhcpReservation", id.ToString());

        return Ok(ToDetail(reservation));
    }

    /// <summary>
    ///     This action adds a new static reservation to a DHCP scope. Rejected with
    ///     <c>409 Conflict</c> when either the client identifier or the IP address is already
    ///     reserved elsewhere in this same scope -- the same "unique per scope per client/per
    ///     address" shape a lease already has, per
    ///     <see cref="Ninlil.Dhcp.Data.Abstractions.Entity.DhcpReservationEntity" />'s own doc
    ///     comment.
    /// </summary>
    [HttpPost]
    [RequirePermission("dhcpscopes:write")]
    [SwaggerRequestExample(
        typeof(DhcpReservationWriteRequest),
        typeof(DhcpReservationWriteRequestExample)
    )]
    [SwaggerResponseExample(StatusCodes.Status201Created, typeof(DhcpReservationDetailExample))]
    public async Task<ActionResult<DhcpReservationDetail>> Create(
        Guid scopeId,
        DhcpReservationWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        DhcpScopeEntity scope = await scopeRepository.GetByIdAsync(scopeId, stoppingToken);
        if (scope is null)
            throw new NotFoundException("DhcpScope", scopeId.ToString());

        if (scope.Kind == DhcpScopeKindEnum.PrefixDelegation)
            throw new ValidationException(
                "A reservation cannot be created against a 'PrefixDelegation' scope -- RFC 8415 "
                    + "defines no delegated-prefix reservation shape of its own."
            );

        if (!TryValidate(request, out string error))
            throw new ValidationException(error);

        if (
            await reservationRepository.FindByClientIdentifierAsync(
                scopeId,
                request.ClientIdentifier,
                stoppingToken
            )
            is not null
        )
            throw new ConflictException(
                $"Client '{request.ClientIdentifier}' already has a reservation in this scope."
            );

        if (
            await reservationRepository.FindByIpAddressAsync(
                scopeId,
                request.IpAddress,
                stoppingToken
            )
            is not null
        )
            throw new ConflictException(
                $"Address '{request.IpAddress}' is already reserved in this scope."
            );

        DhcpReservationEntity reservation = new()
        {
            Id = Guid.NewGuid(),
            ScopeId = scopeId,
            ClientIdentifier = request.ClientIdentifier,
            IpAddress = request.IpAddress,
            HostName = request.HostName,
            IsDisabled = request.IsDisabled,
            Comments = request.Comments,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        await reservationRepository.AddAsync(reservation, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return CreatedAtAction(
            nameof(Get),
            new { scopeId, id = reservation.Id },
            ToDetail(reservation)
        );
    }

    /// <summary>
    ///     This action fully replaces an existing reservation's own fields, keeping its own
    ///     identifier and owning scope.
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("dhcpscopes:write")]
    [SwaggerRequestExample(
        typeof(DhcpReservationWriteRequest),
        typeof(DhcpReservationWriteRequestExample)
    )]
    public async Task<IActionResult> Replace(
        Guid scopeId,
        Guid id,
        DhcpReservationWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        DhcpReservationEntity reservation = await reservationRepository.GetByIdAsync(
            id,
            stoppingToken
        );
        if (reservation is null || reservation.ScopeId != scopeId)
            throw new NotFoundException("DhcpReservation", id.ToString());

        DhcpScopeEntity scope = await scopeRepository.GetByIdAsync(scopeId, stoppingToken);
        if (scope is not null && scope.Kind == DhcpScopeKindEnum.PrefixDelegation)
            throw new ValidationException(
                "A reservation cannot be updated against a 'PrefixDelegation' scope -- RFC 8415 "
                    + "defines no delegated-prefix reservation shape of its own."
            );

        if (!TryValidate(request, out string error))
            throw new ValidationException(error);

        DhcpReservationEntity existingByClient =
            await reservationRepository.FindByClientIdentifierAsync(
                scopeId,
                request.ClientIdentifier,
                stoppingToken
            );
        if (existingByClient is not null && existingByClient.Id != id)
            throw new ConflictException(
                $"Client '{request.ClientIdentifier}' already has a reservation in this scope."
            );

        DhcpReservationEntity existingByAddress = await reservationRepository.FindByIpAddressAsync(
            scopeId,
            request.IpAddress,
            stoppingToken
        );
        if (existingByAddress is not null && existingByAddress.Id != id)
            throw new ConflictException(
                $"Address '{request.IpAddress}' is already reserved in this scope."
            );

        reservation.ClientIdentifier = request.ClientIdentifier;
        reservation.IpAddress = request.IpAddress;
        reservation.HostName = request.HostName;
        reservation.IsDisabled = request.IsDisabled;
        reservation.Comments = request.Comments;
        reservation.UpdatedAtUtc = DateTimeOffset.UtcNow;

        reservationRepository.Update(reservation);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This action removes a reservation from the DHCP scope.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("dhcpscopes:write")]
    public async Task<IActionResult> Delete(Guid scopeId, Guid id, CancellationToken stoppingToken)
    {
        DhcpReservationEntity reservation = await reservationRepository.GetByIdAsync(
            id,
            stoppingToken
        );
        if (reservation is null || reservation.ScopeId != scopeId)
            throw new NotFoundException("DhcpReservation", id.ToString());

        reservationRepository.Remove(reservation);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This method validates a reservation write request's own fields.
    /// </summary>
    private static bool TryValidate(DhcpReservationWriteRequest request, out string error)
    {
        error = null;

        if (request is null || string.IsNullOrWhiteSpace(request.ClientIdentifier))
        {
            error = "'clientIdentifier' is required.";
            return false;
        }

        if (
            string.IsNullOrWhiteSpace(request.IpAddress)
            || !IPAddress.TryParse(request.IpAddress, out _)
        )
        {
            error = $"'{request.IpAddress}' is not a valid IP address.";
            return false;
        }

        return true;
    }

    /// <summary>
    ///     This method converts a reservation entity into its REST representation.
    /// </summary>
    private static DhcpReservationDetail ToDetail(DhcpReservationEntity reservation) =>
        new(
            reservation.Id,
            reservation.ScopeId,
            reservation.ClientIdentifier,
            reservation.IpAddress,
            reservation.HostName,
            reservation.IsDisabled,
            reservation.Comments,
            reservation.CreatedAtUtc,
            reservation.UpdatedAtUtc
        );
}
