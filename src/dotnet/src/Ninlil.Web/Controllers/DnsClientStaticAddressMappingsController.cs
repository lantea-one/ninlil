using System.Net;
using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Web.Examples.ClientStaticAddressMappings;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller administers the manual static address-to-client-identity mapping set --
///     <see cref="DnsClientStaticAddressMappingEntity" />, the flat, top-level entity
///     <c>Ninlil.Dns.Server.ClientIdentity.StaticAddressMappingClientIdentityResolver</c> consults
///     first, ahead of both automatic resolvers, on every real client-identity resolution attempt.
///     This is a flat, non-nested resource with no owning parent, the same
///     <see cref="DnsAccessControlRulesController" />/<see cref="ForwardersController" /> route
///     convention (<c>/api/client-static-address-mappings</c>, no parent segment). Every action
///     requires the <c>clients:read</c> or <c>clients:write</c> permission -- the same permission
///     pair <see cref="ClientsController" /> already uses, since this resource is one more way an
///     operator manages the same "known client" surface.
///     <para>
///         <b>No event publish on write, unlike <see cref="DnsAccessControlRulesController" />.</b>
///         The resolver this controller's writes affect re-queries its own repository fresh on
///         every single call (see that resolver's own doc comment) rather than keeping an
///         in-memory snapshot, so there is no cache to invalidate.
///     </para>
/// </summary>
/// <param name="mappingRepository">The repository this controller reads and writes mappings through.</param>
/// <param name="unitOfWork">The unit of work every staged change is committed through.</param>
[ApiController]
[Route("api/client-static-address-mappings")]
public class DnsClientStaticAddressMappingsController(
    IDnsClientStaticAddressMappingRepository mappingRepository,
    IUnitOfWork unitOfWork
) : ControllerBase
{
    /// <summary>
    ///     This action lists every static address mapping this server knows about, enabled or not.
    /// </summary>
    [HttpGet]
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
    ///     This action looks up a single static address mapping by its identifier.
    /// </summary>
    [HttpGet("{id:guid}")]
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
    ///     This action creates a new static address mapping.
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

        return CreatedAtAction(nameof(Get), new { id = mapping.Id }, ToDetail(mapping));
    }

    /// <summary>
    ///     This action fully replaces an existing static address mapping's own fields.
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

        return NoContent();
    }

    /// <summary>
    ///     This action permanently deletes a static address mapping.
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

        return NoContent();
    }

    /// <summary>
    ///     This method validates a write request's own fields up front, so a <c>POST</c>/<c>PUT</c>
    ///     either fully succeeds or is rejected outright with no partial effect: <see cref="DnsClientStaticAddressMappingWriteRequest.Cidr" />
    ///     must parse as either a bare IP address or a CIDR network (the same dual-format
    ///     acceptance the real resolver matches against), <see cref="DnsClientStaticAddressMappingWriteRequest.ClientIdentifier" />
    ///     must be non-empty, and <see cref="DnsClientStaticAddressMappingWriteRequest.ProtocolVersion" />
    ///     must actually agree with <see cref="DnsClientStaticAddressMappingWriteRequest.Cidr" />'s
    ///     own real address family -- an IPv4 network can never map to an IPv6 identifier space,
    ///     and likewise for IPv6, per <see cref="DnsClientStaticAddressMappingEntity.ProtocolVersion" />'s
    ///     own doc comment.
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
    ///     This method reports a caller-supplied value's own real address family, accepting either
    ///     a bare address or a <c>/</c>-qualified CIDR network -- the same dual-format grammar
    ///     <see cref="DnsClientStaticAddressMappingEntity.Cidr" />'s own doc comment describes.
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
    ///     This method converts a static address mapping entity into its REST representation.
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
