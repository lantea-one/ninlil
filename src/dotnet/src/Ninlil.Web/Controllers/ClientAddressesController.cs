using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.ClientIdentity.Vendor;
using Ninlil.Web.Examples.ClientAddresses;
using Ninlil.Web.Models;
using Ninlil.Web.Routing;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller manages the identifiers (<see cref="DnsClientAddressEntity" />) a known
///     client device answers to, nested under it at <c>/api/clients/{clientId}/addresses</c>.
///     Apart from the first address given to <see cref="ClientCreateRequest" />, this is how a
///     device gains more identifiers; for example, joining a dual-stack device's IPv4 MAC address
///     and IPv6 DUID once an operator has confirmed they are the same hardware, which the server
///     cannot safely infer itself (see
///     <c>Ninlil.Dhcp.Server.DnsIntegration.DhcpClientRegistryAutoPopulationService</c>). Reads
///     need <c>clients:read</c> and changes need <c>clients:write</c>, as for
///     <see cref="ClientsController" />. There is no PUT or PATCH: an address cannot be edited
///     (see <see cref="ClientsController" />), so it is removed and another added instead.
/// </summary>
/// <param name="clientRepository">Confirms the device exists.</param>
/// <param name="clientAddressRepository">Reads and writes addresses.</param>
/// <param name="unitOfWork">Commits staged changes.</param>
/// <param name="vendorLookup">Looks up the vendor of each address's MAC address.</param>
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
    ///     This action lists a device's addresses, one page at a time, each with its MAC vendor.
    ///     Returns 404 for an unknown client. Requires <c>clients:read</c>.
    /// </summary>
    [HttpGet]
    [ReadFromReplica]
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
    ///     This action returns one address. Returns 404 when it does not exist or belongs to
    ///     another client. Requires <c>clients:read</c>.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ReadFromReplica]
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
    ///     This action adds an address to a device. Returns 400 for a missing identifier, 404 for
    ///     an unknown client, and 409 when the identifier and protocol pair already belongs to any
    ///     device, this one included (the pair the address table's unique index covers, checked
    ///     first through <see cref="IDnsClientAddressRepository.GetByIdentifierAsync" />). Requires
    ///     <c>clients:write</c>.
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
    ///     This action removes an address from a device. Returns 409 when it is the device's last
    ///     address, since a device with none could never be matched again; delete the device
    ///     (<see cref="ClientsController.Delete" />) instead. Returns 404 when the address does not
    ///     exist or belongs to another client. Requires <c>clients:write</c>.
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
