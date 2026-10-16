using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Web.Examples.ClientGroupNetworks;
using Ninlil.Web.Models;
using Ninlil.Web.Routing;
using Ninlil.Web.Services;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller manages the members of one client group, nested under it at
///     <c>/api/client-groups/{groupId}/networks</c>. Each member is either a CIDR network
///     (<see cref="DnsClientGroupNetworkEntity.Cidr" />) or a DNS-over-HTTPS ClientID
///     (<see cref="DnsClientGroupNetworkEntity.ClientId" />), never both. Reads need
///     <c>clientgroups:read</c> and changes need <c>clientgroups:write</c>, as for
///     <see cref="ClientGroupsController" />. There is no PATCH: a member has one meaningful
///     field, so a partial update would be the same as a PUT.
/// </summary>
/// <param name="clientGroupRepository">Confirms the group exists.</param>
/// <param name="networkRepository">Reads and writes members.</param>
/// <param name="unitOfWork">Commits staged changes.</param>
[ApiController]
[Route("api/client-groups/{groupId:guid}/networks")]
public class ClientGroupNetworksController(
    IDnsClientGroupRepository clientGroupRepository,
    IDnsClientGroupNetworkRepository networkRepository,
    IUnitOfWork unitOfWork
) : ControllerBase
{
    /// <summary>
    ///     This action lists a group's members, one page at a time. Returns 404 for an unknown
    ///     group. Requires <c>clientgroups:read</c>.
    /// </summary>
    [HttpGet]
    [ReadFromReplica]
    [RequirePermission("clientgroups:read")]
    [SwaggerResponseExample(
        StatusCodes.Status200OK,
        typeof(ClientGroupNetworkDetailPagedResultExample)
    )]
    public async Task<ActionResult<PagedResult<ClientGroupNetworkDetail>>> List(
        Guid groupId,
        [FromQuery] PaginationQuery pagination,
        CancellationToken stoppingToken
    )
    {
        if (await clientGroupRepository.GetByIdAsync(groupId, stoppingToken) is null)
            throw new NotFoundException("ClientGroup", groupId.ToString());

        (int skip, int take) = (pagination ?? new PaginationQuery()).Normalize();

        IReadOnlyList<DnsClientGroupNetworkEntity> networks =
            await networkRepository.SearchByGroupIdAsync(groupId, skip, take, stoppingToken);
        int totalCount = await networkRepository.CountByGroupIdAsync(groupId, stoppingToken);

        return Ok(
            new PagedResult<ClientGroupNetworkDetail>(
                [.. networks.Select(ToDetail)],
                skip,
                take,
                totalCount
            )
        );
    }

    /// <summary>
    ///     This action returns one member. Returns 404 when it does not exist or belongs to another
    ///     group. Requires <c>clientgroups:read</c>.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ReadFromReplica]
    [RequirePermission("clientgroups:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ClientGroupNetworkDetailExample))]
    public async Task<ActionResult<ClientGroupNetworkDetail>> Get(
        Guid groupId,
        Guid id,
        CancellationToken stoppingToken
    )
    {
        DnsClientGroupNetworkEntity network = await networkRepository.GetByIdAsync(
            id,
            stoppingToken
        );
        if (network is null || network.ClientGroupId != groupId)
            throw new NotFoundException("ClientGroupNetwork", id.ToString());

        return Ok(ToDetail(network));
    }

    /// <summary>
    ///     This action adds a member to the group. Returns 400 unless exactly one of <c>cidr</c>
    ///     and <c>clientId</c> is given or when the address or CIDR network does not parse, and 404 for an unknown
    ///     group. Requires <c>clientgroups:write</c>.
    /// </summary>
    [HttpPost]
    [RequirePermission("clientgroups:write")]
    [SwaggerRequestExample(
        typeof(ClientGroupNetworkWriteRequest),
        typeof(ClientGroupNetworkWriteRequestExample)
    )]
    [SwaggerResponseExample(StatusCodes.Status201Created, typeof(ClientGroupNetworkDetailExample))]
    public async Task<ActionResult<ClientGroupNetworkDetail>> Create(
        Guid groupId,
        ClientGroupNetworkWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        if (await clientGroupRepository.GetByIdAsync(groupId, stoppingToken) is null)
            throw new NotFoundException("ClientGroup", groupId.ToString());

        if (!TryValidateRequest(request?.Cidr, request?.ClientId, out string error))
            throw new ValidationException(error);

        DnsClientGroupNetworkEntity network = new()
        {
            Id = Guid.NewGuid(),
            ClientGroupId = groupId,
            Cidr = request.Cidr?.Trim(),
            ClientId = request.ClientId,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        await networkRepository.AddAsync(network, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return CreatedAtAction(nameof(Get), new { groupId, id = network.Id }, ToDetail(network));
    }

    /// <summary>
    ///     This action replaces a member's CIDR or ClientID, keeping its id. Validation is as for
    ///     <see cref="Create" />; returns 404 when the member does not exist or belongs to another
    ///     group. Requires <c>clientgroups:write</c>.
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("clientgroups:write")]
    [SwaggerRequestExample(
        typeof(ClientGroupNetworkWriteRequest),
        typeof(ClientGroupNetworkWriteRequestExample)
    )]
    public async Task<IActionResult> Replace(
        Guid groupId,
        Guid id,
        ClientGroupNetworkWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        DnsClientGroupNetworkEntity network = await networkRepository.GetByIdAsync(
            id,
            stoppingToken
        );
        if (network is null || network.ClientGroupId != groupId)
            throw new NotFoundException("ClientGroupNetwork", id.ToString());

        if (!TryValidateRequest(request?.Cidr, request?.ClientId, out string error))
            throw new ValidationException(error);

        network.Cidr = request.Cidr?.Trim();
        network.ClientId = request.ClientId;

        networkRepository.Update(network);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This action removes a member from the group. Returns 404 when it does not exist or
    ///     belongs to another group. Requires <c>clientgroups:write</c>.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("clientgroups:write")]
    public async Task<IActionResult> Delete(Guid groupId, Guid id, CancellationToken stoppingToken)
    {
        DnsClientGroupNetworkEntity network = await networkRepository.GetByIdAsync(
            id,
            stoppingToken
        );
        if (network is null || network.ClientGroupId != groupId)
            throw new NotFoundException("ClientGroupNetwork", id.ToString());

        networkRepository.Remove(network);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This method checks that exactly one of <c>Cidr</c> and <c>ClientId</c> is set and that a
    ///     value is a bare IP address or a CIDR network (<see cref="NetworkEntryFormat" />), the
    ///     forms <c>ClientAddress.NetworkContains</c> matches, so an accepted entry can match. A ClientID is an operator-chosen label matched
    ///     by case-insensitive equality, so it has no format to check.
    /// </summary>
    private static bool TryValidateRequest(string cidr, string clientId, out string error)
    {
        error = null;

        bool hasCidr = !string.IsNullOrWhiteSpace(cidr);
        bool hasClientId = !string.IsNullOrWhiteSpace(clientId);

        if (hasCidr == hasClientId)
        {
            error = hasCidr
                ? "Exactly one of 'cidr'/'clientId' may be set, not both."
                : "Exactly one of 'cidr'/'clientId' is required.";
            return false;
        }

        if (hasCidr && !NetworkEntryFormat.TryParse(cidr, out _))
        {
            error = string.Format(NetworkEntryFormat.InvalidMessageFormat, cidr);
            return false;
        }

        return true;
    }

    /// <summary>
    ///     This method converts a group member entity to its API representation.
    /// </summary>
    private static ClientGroupNetworkDetail ToDetail(DnsClientGroupNetworkEntity network) =>
        new(
            network.Id,
            network.ClientGroupId,
            network.Cidr,
            network.ClientId,
            network.CreatedAtUtc
        );
}
