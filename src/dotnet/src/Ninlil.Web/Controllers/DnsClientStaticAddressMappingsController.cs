using System.Net;
using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.ClientIdentity;
using Ninlil.Web.Examples.ClientStaticAddressMappings;
using Ninlil.Web.Models;
using Ninlil.Web.Routing;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller manages static address mappings
///     (<see cref="DnsClientStaticAddressMappingEntity" />): manual rules that say which client
///     identifier an IP address or network belongs to.
///     <c>Ninlil.Dns.Server.ClientIdentity.StaticAddressMappingClientIdentityResolver</c> checks
///     them first, before the automatic resolvers, whenever a client's identity is resolved. The
///     route is flat (<c>/api/client-static-address-mappings</c>). Reads need <c>clients:read</c>
///     and changes need <c>clients:write</c>, as for <see cref="ClientsController" />, since this
///     is part of managing known clients.
///     <para>
///         <b>No event is published on a write</b> (unlike
///         <see cref="DnsAccessControlRulesController" />). The resolver keeps an in-memory
///         snapshot of the enabled mappings, so every write waits for
///         <see cref="StaticAddressMappingClientIdentityResolver.RefreshAsync" /> after the save,
///         and the next query sees the change. A failed refresh is logged by the resolver and does
///         not fail the request; the snapshot then catches up at its next refresh.
///     </para>
/// </summary>
/// <param name="mappingRepository">Reads and writes mappings.</param>
/// <param name="unitOfWork">Commits staged changes.</param>
/// <param name="resolver">The resolver whose snapshot is refreshed after every write.</param>
[ApiController]
[Route("api/client-static-address-mappings")]
public class DnsClientStaticAddressMappingsController(
    IDnsClientStaticAddressMappingRepository mappingRepository,
    IUnitOfWork unitOfWork,
    StaticAddressMappingClientIdentityResolver resolver
) : ControllerBase
{
    /// <summary>
    ///     This action lists static address mappings, enabled or not, one page at a time. Requires
    ///     <c>clients:read</c>.
    /// </summary>
    [HttpGet]
    [ReadFromReplica]
    [RequirePermission("clients:read")]
    [SwaggerResponseExample(
        StatusCodes.Status200OK,
        typeof(DnsClientStaticAddressMappingDetailPagedResultExample)
    )]
    public async Task<ActionResult<PagedResult<DnsClientStaticAddressMappingDetail>>> List(
        [FromQuery] PaginationQuery pagination,
        CancellationToken stoppingToken
    )
    {
        (int skip, int take) = (pagination ?? new PaginationQuery()).Normalize();

        IReadOnlyList<DnsClientStaticAddressMappingEntity> mappings =
            await mappingRepository.SearchAsync(skip, take, stoppingToken);
        int totalCount = await mappingRepository.CountAsync(stoppingToken);

        return Ok(
            new PagedResult<DnsClientStaticAddressMappingDetail>(
                [.. mappings.Select(ToDetail)],
                skip,
                take,
                totalCount
            )
        );
    }

    /// <summary>
    ///     This action returns one static address mapping. Returns 404 for an unknown id. Requires
    ///     <c>clients:read</c>.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ReadFromReplica]
    [RequirePermission("clients:read")]
    [SwaggerResponseExample(
        StatusCodes.Status200OK,
        typeof(DnsClientStaticAddressMappingDetailExample)
    )]
    public async Task<ActionResult<DnsClientStaticAddressMappingDetail>> Get(
        Guid id,
        CancellationToken stoppingToken
    )
    {
        DnsClientStaticAddressMappingEntity mapping = await mappingRepository.GetByIdAsync(
            id,
            stoppingToken
        );
        if (mapping is null)
            throw new NotFoundException("ClientStaticAddressMapping", id.ToString());

        return Ok(ToDetail(mapping));
    }

    /// <summary>
    ///     This action creates a static address mapping. Returns 400 for a missing identifier, an
    ///     address or network that does not parse, or a protocol version that does not match the
    ///     address family. Requires <c>clients:write</c>.
    /// </summary>
    [HttpPost]
    [RequirePermission("clients:write")]
    [SwaggerRequestExample(
        typeof(DnsClientStaticAddressMappingWriteRequest),
        typeof(DnsClientStaticAddressMappingWriteRequestExample)
    )]
    [SwaggerResponseExample(
        StatusCodes.Status201Created,
        typeof(DnsClientStaticAddressMappingDetailExample)
    )]
    public async Task<ActionResult<DnsClientStaticAddressMappingDetail>> Create(
        DnsClientStaticAddressMappingWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        if (!TryValidate(request, out string error))
            throw new ValidationException(error);

        DnsClientStaticAddressMappingEntity mapping = new()
        {
            Id = Guid.NewGuid(),
            Cidr = request.Cidr,
            ClientIdentifier = request.ClientIdentifier,
            ProtocolVersion = request.ProtocolVersion,
            IsDisabled = request.IsDisabled,
            Comments = request.Comments,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        await mappingRepository.AddAsync(mapping, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);
        await resolver.RefreshAsync(stoppingToken);

        return CreatedAtAction(nameof(Get), new { id = mapping.Id }, ToDetail(mapping));
    }

    /// <summary>
    ///     This action replaces a static address mapping's fields. Validation is as for
    ///     <see cref="Create" />; returns 404 for an unknown id. Requires <c>clients:write</c>.
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("clients:write")]
    [SwaggerRequestExample(
        typeof(DnsClientStaticAddressMappingWriteRequest),
        typeof(DnsClientStaticAddressMappingWriteRequestExample)
    )]
    public async Task<IActionResult> Replace(
        Guid id,
        DnsClientStaticAddressMappingWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        DnsClientStaticAddressMappingEntity mapping = await mappingRepository.GetByIdAsync(
            id,
            stoppingToken
        );
        if (mapping is null)
            throw new NotFoundException("ClientStaticAddressMapping", id.ToString());

        if (!TryValidate(request, out string error))
            throw new ValidationException(error);

        mapping.Cidr = request.Cidr;
        mapping.ClientIdentifier = request.ClientIdentifier;
        mapping.ProtocolVersion = request.ProtocolVersion;
        mapping.IsDisabled = request.IsDisabled;
        mapping.Comments = request.Comments;
        mapping.UpdatedAtUtc = DateTimeOffset.UtcNow;

        mappingRepository.Update(mapping);
        await unitOfWork.SaveChangesAsync(stoppingToken);
        await resolver.RefreshAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This action deletes a static address mapping. Returns 404 for an unknown id. Requires
    ///     <c>clients:write</c>.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("clients:write")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken stoppingToken)
    {
        DnsClientStaticAddressMappingEntity mapping = await mappingRepository.GetByIdAsync(
            id,
            stoppingToken
        );
        if (mapping is null)
            throw new NotFoundException("ClientStaticAddressMapping", id.ToString());

        mappingRepository.Remove(mapping);
        await unitOfWork.SaveChangesAsync(stoppingToken);
        await resolver.RefreshAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This method checks a write request before anything is saved:
    ///     <see cref="DnsClientStaticAddressMappingWriteRequest.Cidr" /> must be a bare IP address
    ///     or a CIDR network (the two forms the resolver matches),
    ///     <see cref="DnsClientStaticAddressMappingWriteRequest.ClientIdentifier" /> must not be
    ///     empty, and <see cref="DnsClientStaticAddressMappingWriteRequest.ProtocolVersion" /> must
    ///     match the address family, since an IPv4 network cannot map into the IPv6 identifier
    ///     space or the reverse (see
    ///     <see cref="DnsClientStaticAddressMappingEntity.ProtocolVersion" />).
    /// </summary>
    private static bool TryValidate(
        DnsClientStaticAddressMappingWriteRequest request,
        out string error
    )
    {
        error = null;

        if (request is null || string.IsNullOrWhiteSpace(request.ClientIdentifier))
        {
            error = "'clientIdentifier' is required.";
            return false;
        }

        if (!TryParseAddressFamily(request.Cidr, out System.Net.Sockets.AddressFamily family))
        {
            error = $"'{request.Cidr}' is not a valid IP address or CIDR network.";
            return false;
        }

        bool isIpv4 = family == System.Net.Sockets.AddressFamily.InterNetwork;
        bool expectsIpv4 = request.ProtocolVersion == InternetProtocolVersionEnum.IPv4;

        if (isIpv4 != expectsIpv4)
        {
            error =
                $"'cidr' is a{(isIpv4 ? "n IPv4" : "n IPv6")} network, but 'protocolVersion' is "
                + $"'{request.ProtocolVersion}' -- the two must agree.";
            return false;
        }

        return true;
    }

    /// <summary>
    ///     This method reports the address family of a bare address or a <c>/</c>-qualified CIDR
    ///     network, the two forms <see cref="DnsClientStaticAddressMappingEntity.Cidr" /> accepts.
    /// </summary>
    private static bool TryParseAddressFamily(
        string value,
        out System.Net.Sockets.AddressFamily family
    )
    {
        family = default;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (value.Contains('/'))
        {
            if (!IPNetwork.TryParse(value, out IPNetwork network))
                return false;

            family = network.BaseAddress.AddressFamily;
            return true;
        }

        if (!IPAddress.TryParse(value, out IPAddress address))
            return false;

        family = address.AddressFamily;
        return true;
    }

    /// <summary>
    ///     This method converts a mapping entity to its API representation.
    /// </summary>
    private static DnsClientStaticAddressMappingDetail ToDetail(
        DnsClientStaticAddressMappingEntity mapping
    ) =>
        new(
            mapping.Id,
            mapping.Cidr,
            mapping.ClientIdentifier,
            mapping.ProtocolVersion,
            mapping.IsDisabled,
            mapping.Comments,
            mapping.CreatedAtUtc,
            mapping.UpdatedAtUtc
        );
}
