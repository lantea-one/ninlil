using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Web.Examples.ClientGroupNetworks;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller administers the CIDR networks that match clients into a single
///     client group, nested under it (<c>/api/client-groups/{groupId}/networks</c>) since a
///     network only ever makes sense in its owning group's own context -- the same nesting
///     <see cref="RecordsController" /> already establishes for a zone's records. Every action
///     requires the <c>clientgroups:read</c> or <c>clientgroups:write</c> permission, the same as
///     <see cref="ClientGroupsController" />. There is deliberately no <c>PATCH</c> here -- a
///     network has exactly one meaningful field at a time (either
///     <see cref="DnsClientGroupNetworkEntity.Cidr" /> or
///     <see cref="DnsClientGroupNetworkEntity.ClientId" />), so a partial update would be
///     indistinguishable from a full <c>PUT</c>.
/// </summary>
/// <param name="clientGroupRepository">The repository used to confirm the owning group exists.</param>
/// <param name="networkRepository">The repository this controller reads and writes networks through directly.</param>
/// <param name="unitOfWork">The unit of work every staged change is committed through.</param>
[ApiController]
[Route("api/client-groups/{groupId:guid}/networks")]
public class ClientGroupNetworksController(
    IDnsClientGroupRepository clientGroupRepository,
    IDnsClientGroupNetworkRepository networkRepository,
    IUnitOfWork unitOfWork
) : ControllerBase
{
    /// <summary>
    ///     This action lists every network belonging to a client group.
    /// </summary>
    [HttpGet]
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
    ///     This action looks up a single network by its identifier.
    /// </summary>
    [HttpGet("{id:guid}")]
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
    ///     This action adds a new network to the client group.
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
            Cidr = request.Cidr,
            ClientId = request.ClientId,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        await networkRepository.AddAsync(network, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return CreatedAtAction(nameof(Get), new { groupId, id = network.Id }, ToDetail(network));
    }

    /// <summary>
    ///     This action fully replaces an existing network's own CIDR value, keeping its own
    ///     identifier.
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

        network.Cidr = request.Cidr;
        network.ClientId = request.ClientId;

        networkRepository.Update(network);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This action removes a network from the client group.
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
    ///     This method validates a write request against this entity's own "exactly one of
    ///     <c>Cidr</c>/<c>ClientId</c>" rule, and -- when a CIDR was supplied -- that it at least
    ///     parses as an IP network, via <c>System.Net.IPNetwork</c>'s own parser, matching what
    ///     <c>DnsFilterEngine</c> itself uses at match time, so an entry this controller accepts
    ///     is guaranteed to actually be usable by the engine rather than silently never matching
    ///     anything. A <c>ClientId</c> has no comparable format to validate -- it is an
    ///     operator-chosen free-text label, matched only by exact (case-insensitive) equality.
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

        if (hasCidr && !System.Net.IPNetwork.TryParse(cidr, out _))
        {
            error = $"'{cidr}' is not a valid IP address or CIDR network.";
            return false;
        }

        return true;
    }

    /// <summary>
    ///     This method converts a client group network entity into its REST representation.
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
