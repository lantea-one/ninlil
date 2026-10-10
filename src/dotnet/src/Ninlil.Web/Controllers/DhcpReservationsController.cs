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
using Ninlil.Web.Routing;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller manages the static reservations of one DHCP scope, nested under it at
///     <c>/api/dhcp-scopes/{scopeId}/reservations</c>. Reads need <c>dhcpscopes:read</c> and
///     changes need <c>dhcpscopes:write</c>, as for <see cref="DhcpScopesController" />.
///     <para>
///         <b>Address scopes only.</b> RFC 8415 has no form for reserving a delegated prefix; a
///         reservation is always one fixed address. <see cref="Create" /> and
///         <see cref="Replace" /> therefore return 400 for a
///         <see cref="DhcpScopeKindEnum.PrefixDelegation" /> scope rather than accept a
///         reservation the server could not honor.
///     </para>
/// </summary>
/// <param name="scopeRepository">Confirms the scope exists and supplies its <see cref="DhcpScopeEntity.Kind" />.</param>
/// <param name="reservationRepository">Reads and writes reservations.</param>
/// <param name="unitOfWork">Commits staged changes.</param>
[ApiController]
[Route("api/dhcp-scopes/{scopeId:guid}/reservations")]
public class DhcpReservationsController(
    IDhcpScopeRepository scopeRepository,
    IDhcpReservationRepository reservationRepository,
    IUnitOfWork unitOfWork
) : ControllerBase
{
    /// <summary>
    ///     This action lists a scope's reservations, one page at a time. Returns 404 for an unknown
    ///     scope. Requires <c>dhcpscopes:read</c>.
    /// </summary>
    [HttpGet]
    [ReadFromReplica]
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
    ///     This action returns one reservation. Returns 404 when it does not exist or belongs to
    ///     another scope. Requires <c>dhcpscopes:read</c>.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ReadFromReplica]
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
    ///     This action adds a static reservation to a scope. Returns 400 for a prefix-delegation
    ///     scope, a missing client identifier or an invalid IP address; 404 for an unknown scope;
    ///     and 409 when the client identifier or the address is already reserved in this scope
    ///     (both are unique per scope, as for leases; see
    ///     <see cref="Ninlil.Dhcp.Data.Abstractions.Entity.DhcpReservationEntity" />). The address
    ///     is not checked against the scope's range or protocol. Requires <c>dhcpscopes:write</c>.
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
    ///     This action replaces a reservation's fields, keeping its id and scope. Validation and
    ///     errors are as for <see cref="Create" />. Requires <c>dhcpscopes:write</c>.
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
    ///     This action removes a reservation from the scope. Returns 404 when it does not exist or
    ///     belongs to another scope. Requires <c>dhcpscopes:write</c>.
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
    ///     This method checks that a reservation request has a client identifier and a valid IP
    ///     address.
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
    ///     This method converts a reservation entity to its API representation.
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
