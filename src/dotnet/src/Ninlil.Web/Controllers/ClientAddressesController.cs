using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.ClientIdentity.Vendor;
using Ninlil.Web.Examples.ClientAddresses;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller administers which persistent identifiers (<see cref="DnsClientAddressEntity" />)
///     a single known client device answers to, nested under it
///     (<c>/api/clients/{clientId}/addresses</c>) since an address only ever makes sense in its
///     owning device's own context -- the same nesting <see cref="ClientListAssignmentsController" />/
///     <see cref="ClientSchedulesController" /> already establish for a client's own other child
///     resources. This is the only way, alongside <see cref="ClientCreateRequest" />'s own initial
///     address, that a device ever gains a further identifier -- for instance, linking a
///     genuinely dual-stack device's own separate IPv4 MAC address and IPv6 DUID together under one
///     device once an operator has confirmed both belong to the same real hardware, something no
///     resolver in this project can safely infer on its own (see
///     <c>Ninlil.Dhcp.Server.DnsIntegration.DhcpClientRegistryAutoPopulationService</c>'s own doc
///     comment for why). Every action requires the <c>clients:read</c> or <c>clients:write</c>
///     permission, the same as <see cref="ClientsController" />. There is deliberately no
///     <c>PUT</c>/<c>PATCH</c> here -- an address's own identifier/protocol pair is immutable once
///     created (see <see cref="ClientsController" />'s own doc comment); changing it means removing
///     one address and adding another.
/// </summary>
/// <param name="clientRepository">The repository used to confirm the owning device exists.</param>
/// <param name="clientAddressRepository">The repository this controller reads and writes addresses through directly.</param>
/// <param name="unitOfWork">The unit of work every staged change is committed through.</param>
/// <param name="vendorLookup">The MAC vendor lookup each address's vendor fields are computed through.</param>
[ApiController]
[Route("api/clients/{clientId:guid}/addresses")]
public class ClientAddressesController(
    IDnsClientRepository clientRepository,
    IDnsClientAddressRepository clientAddressRepository,
    IUnitOfWork unitOfWork,
    IMacVendorLookupService vendorLookup
) : ControllerBase
{
    /// <summary>
    ///     This action lists every address belonging to a known client device.
    /// </summary>
    [HttpGet]
    [RequirePermission("clients:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ClientAddressDetailPagedResultExample))]
    public async Task<ActionResult<PagedResult<ClientAddressDetail>>> List(
        Guid clientId,
        [FromQuery] PaginationQuery pagination,
        CancellationToken stoppingToken
    )
    {
        if (await clientRepository.GetByIdAsync(clientId, stoppingToken) is null)
            throw new NotFoundException("Client", clientId.ToString());

        (int skip, int take) = (pagination ?? new PaginationQuery()).Normalize();

        IReadOnlyList<DnsClientAddressEntity> addresses =
            await clientAddressRepository.SearchByClientIdAsync(
                clientId,
                skip,
                take,
                stoppingToken
            );
        int totalCount = await clientAddressRepository.CountByClientIdAsync(
            clientId,
            stoppingToken
        );

        return Ok(
            new PagedResult<ClientAddressDetail>(
                [
                    .. addresses.Select(address =>
                        ClientsController.ToAddressDetail(address, vendorLookup)
                    ),
                ],
                skip,
                take,
                totalCount
            )
        );
    }

    /// <summary>
    ///     This action looks up a single address by its identifier.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("clients:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ClientAddressDetailExample))]
    public async Task<ActionResult<ClientAddressDetail>> Get(
        Guid clientId,
        Guid id,
        CancellationToken stoppingToken
    )
    {
        DnsClientAddressEntity address = await clientAddressRepository.GetByIdAsync(
            id,
            stoppingToken
        );
        if (address is null || address.ClientId != clientId)
            throw new NotFoundException("ClientAddress", id.ToString());

        return Ok(ClientsController.ToAddressDetail(address, vendorLookup));
    }

    /// <summary>
    ///     This action adds a further address to the known client device. Rejected with
    ///     <c>409 Conflict</c> when this exact identifier/protocol pair already belongs to any
    ///     device (this one included) -- the same real, storage-level uniqueness
    ///     <see cref="DnsClientAddressEntity" />'s own composite index enforces, translated here via
    ///     a pre-check against <see cref="IDnsClientAddressRepository.GetByIdentifierAsync" />,
    ///     mirroring <see cref="ClientsController.Create" />'s own identical pre-check.
    /// </summary>
    [HttpPost]
    [RequirePermission("clients:write")]
    [SwaggerRequestExample(
        typeof(ClientAddressWriteRequest),
        typeof(ClientAddressWriteRequestExample)
    )]
    [SwaggerResponseExample(StatusCodes.Status201Created, typeof(ClientAddressDetailExample))]
    public async Task<ActionResult<ClientAddressDetail>> Create(
        Guid clientId,
        ClientAddressWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        if (await clientRepository.GetByIdAsync(clientId, stoppingToken) is null)
            throw new NotFoundException("Client", clientId.ToString());

        if (string.IsNullOrWhiteSpace(request?.ClientIdentifier))
            throw new ValidationException("'clientIdentifier' is required.");

        if (
            await clientAddressRepository.GetByIdentifierAsync(
                request.ClientIdentifier,
                request.ProtocolVersion,
                stoppingToken
            )
            is not null
        )
            throw new ConflictException(
                $"An address already exists for identifier '{request.ClientIdentifier}' ({request.ProtocolVersion})."
            );

        DateTimeOffset now = DateTimeOffset.UtcNow;

        DnsClientAddressEntity address = new()
        {
            Id = Guid.NewGuid(),
            ClientId = clientId,
            ClientIdentifier = request.ClientIdentifier,
            ProtocolVersion = request.ProtocolVersion,
            FirstSeenUtc = now,
            CreatedAtUtc = now,
        };

        await clientAddressRepository.AddAsync(address, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return CreatedAtAction(
            nameof(Get),
            new { clientId, id = address.Id },
            ClientsController.ToAddressDetail(address, vendorLookup)
        );
    }

    /// <summary>
    ///     This action removes an address from the known client device. Rejected with
    ///     <c>409 Conflict</c> when this is the device's own last remaining address -- a device
    ///     with no address at all could never be resolved by anything again, so removing one always
    ///     leaves at least one behind; delete the whole device (<see cref="ClientsController.Delete" />)
    ///     instead when it should no longer be tracked at all.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("clients:write")]
    public async Task<IActionResult> Delete(Guid clientId, Guid id, CancellationToken stoppingToken)
    {
        DnsClientAddressEntity address = await clientAddressRepository.GetByIdAsync(
            id,
            stoppingToken
        );
        if (address is null || address.ClientId != clientId)
            throw new NotFoundException("ClientAddress", id.ToString());

        int remainingCount = await clientAddressRepository.CountByClientIdAsync(
            clientId,
            stoppingToken
        );
        if (remainingCount <= 1)
            throw new ConflictException(
                "This is the client's own last remaining address -- delete the client itself instead of leaving it with none."
            );

        clientAddressRepository.Remove(address);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }
}
