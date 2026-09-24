using Microsoft.AspNetCore.Mvc;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Web.Examples.DhcpScopeCustomOptions;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller administers the arbitrary, raw DHCPv4 options belonging to a single DHCP
///     scope, nested under it (<c>/api/dhcp-scopes/{scopeId}/custom-options</c>) since a custom
///     option only ever makes sense in its owning scope's own context -- the same nesting
///     <see cref="RecordsController" /> already establishes for a zone's records. Every action
///     requires the <c>dhcpscopes:read</c> or <c>dhcpscopes:write</c> permission, the same as
///     <see cref="DhcpScopesController" />.
/// </summary>
/// <param name="scopeRepository">The repository used to confirm the owning scope exists.</param>
/// <param name="customOptionRepository">The repository this controller reads and writes custom options through directly.</param>
/// <param name="unitOfWork">The unit of work every staged change is committed through.</param>
[ApiController]
[Route("api/dhcp-scopes/{scopeId:guid}/custom-options")]
public class DhcpScopeCustomOptionsController(
    IDhcpScopeRepository scopeRepository,
    IDhcpScopeCustomOptionRepository customOptionRepository,
    IUnitOfWork unitOfWork
) : ControllerBase
{
    /// <summary>
    ///     This action lists every custom option belonging to a DHCP scope.
    /// </summary>
    [HttpGet]
    [RequirePermission("dhcpscopes:read")]
    [SwaggerResponseExample(
        StatusCodes.Status200OK,
        typeof(DhcpScopeCustomOptionDetailPagedResultExample)
    )]
    public async Task<ActionResult<PagedResult<DhcpScopeCustomOptionDetail>>> List(
        Guid scopeId,
        [FromQuery] PaginationQuery pagination,
        CancellationToken stoppingToken
    )
    {
        if (await scopeRepository.GetByIdAsync(scopeId, stoppingToken) is null)
            throw new NotFoundException("DhcpScope", scopeId.ToString());

        (int skip, int take) = (pagination ?? new PaginationQuery()).Normalize();

        IReadOnlyList<DhcpScopeCustomOptionEntity> customOptions =
            await customOptionRepository.SearchByScopeIdAsync(scopeId, skip, take, stoppingToken);
        int totalCount = await customOptionRepository.CountByScopeIdAsync(scopeId, stoppingToken);

        return Ok(
            new PagedResult<DhcpScopeCustomOptionDetail>(
                [.. customOptions.Select(ToDetail)],
                skip,
                take,
                totalCount
            )
        );
    }

    /// <summary>
    ///     This action looks up a single custom option by its identifier.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("dhcpscopes:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(DhcpScopeCustomOptionDetailExample))]
    public async Task<ActionResult<DhcpScopeCustomOptionDetail>> Get(
        Guid scopeId,
        Guid id,
        CancellationToken stoppingToken
    )
    {
        DhcpScopeCustomOptionEntity customOption = await customOptionRepository.GetByIdAsync(
            id,
            stoppingToken
        );
        if (customOption is null || customOption.ScopeId != scopeId)
            throw new NotFoundException("DhcpScopeCustomOption", id.ToString());

        return Ok(ToDetail(customOption));
    }

    /// <summary>
    ///     This action adds a new custom option to a DHCP scope. See
    ///     <see cref="Ninlil.Dhcp.Data.Abstractions.Entity.DhcpScopeCustomOptionEntity" />'s own doc
    ///     comment for how a custom option interacts with this project's own standard-option
    ///     computation and a client's Parameter Request List.
    /// </summary>
    [HttpPost]
    [RequirePermission("dhcpscopes:write")]
    [SwaggerRequestExample(
        typeof(DhcpScopeCustomOptionWriteRequest),
        typeof(DhcpScopeCustomOptionWriteRequestExample)
    )]
    [SwaggerResponseExample(
        StatusCodes.Status201Created,
        typeof(DhcpScopeCustomOptionDetailExample)
    )]
    public async Task<ActionResult<DhcpScopeCustomOptionDetail>> Create(
        Guid scopeId,
        DhcpScopeCustomOptionWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        if (await scopeRepository.GetByIdAsync(scopeId, stoppingToken) is null)
            throw new NotFoundException("DhcpScope", scopeId.ToString());

        if (!TryValidate(request, out string normalizedValueHex, out string error))
            throw new ValidationException(error);

        DhcpScopeCustomOptionEntity customOption = new()
        {
            Id = Guid.NewGuid(),
            ScopeId = scopeId,
            OptionTag = request.OptionTag,
            ValueHex = normalizedValueHex,
            IsDisabled = request.IsDisabled,
            ValueSource = request.ValueSource,
            Comments = request.Comments,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        await customOptionRepository.AddAsync(customOption, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return CreatedAtAction(
            nameof(Get),
            new { scopeId, id = customOption.Id },
            ToDetail(customOption)
        );
    }

    /// <summary>
    ///     This action fully replaces an existing custom option's own fields, keeping its own
    ///     identifier and owning scope.
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("dhcpscopes:write")]
    [SwaggerRequestExample(
        typeof(DhcpScopeCustomOptionWriteRequest),
        typeof(DhcpScopeCustomOptionWriteRequestExample)
    )]
    public async Task<IActionResult> Replace(
        Guid scopeId,
        Guid id,
        DhcpScopeCustomOptionWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        DhcpScopeCustomOptionEntity customOption = await customOptionRepository.GetByIdAsync(
            id,
            stoppingToken
        );
        if (customOption is null || customOption.ScopeId != scopeId)
            throw new NotFoundException("DhcpScopeCustomOption", id.ToString());

        if (!TryValidate(request, out string normalizedValueHex, out string error))
            throw new ValidationException(error);

        customOption.OptionTag = request.OptionTag;
        customOption.ValueHex = normalizedValueHex;
        customOption.IsDisabled = request.IsDisabled;
        customOption.ValueSource = request.ValueSource;
        customOption.Comments = request.Comments;
        customOption.UpdatedAtUtc = DateTimeOffset.UtcNow;

        customOptionRepository.Update(customOption);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This action removes a custom option from the DHCP scope.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("dhcpscopes:write")]
    public async Task<IActionResult> Delete(Guid scopeId, Guid id, CancellationToken stoppingToken)
    {
        DhcpScopeCustomOptionEntity customOption = await customOptionRepository.GetByIdAsync(
            id,
            stoppingToken
        );
        if (customOption is null || customOption.ScopeId != scopeId)
            throw new NotFoundException("DhcpScopeCustomOption", id.ToString());

        customOptionRepository.Remove(customOption);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This method validates a custom option write request, and canonicalizes
    ///     <see cref="DhcpScopeCustomOptionWriteRequest.ValueHex" /> to the uppercase, even-length
    ///     hexadecimal form <see cref="Ninlil.Dhcp.Data.Abstractions.Entity.DhcpScopeCustomOptionEntity.ValueHex" />
    ///     always stores. A blank <c>valueHex</c> is only ever rejected for
    ///     <see cref="DhcpOptionValueSourceEnum.Custom" /> (this option's stored value is what gets
    ///     delivered, so it must actually contain something); for
    ///     <see cref="DhcpOptionValueSourceEnum.ServerAddress" /> it is genuinely irrelevant --
    ///     the delivery engine substitutes this server's own address instead and never reads it --
    ///     so a blank value is accepted and normalized to an empty string rather than rejected.
    /// </summary>
    private static bool TryValidate(
        DhcpScopeCustomOptionWriteRequest request,
        out string normalizedValueHex,
        out string error
    )
    {
        normalizedValueHex = null;
        error = null;

        if (request is null)
        {
            error = "A request body is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.ValueHex))
        {
            if (request.ValueSource == DhcpOptionValueSourceEnum.ServerAddress)
            {
                normalizedValueHex = string.Empty;
                return true;
            }

            error = "'valueHex' is required.";
            return false;
        }

        try
        {
            normalizedValueHex = Convert.ToHexString(Convert.FromHexString(request.ValueHex));
        }
        catch (FormatException)
        {
            error =
                $"'{request.ValueHex}' is not a valid hexadecimal string -- it must have an "
                + "even number of characters, using only 0-9 and A-F.";
            return false;
        }

        return true;
    }

    /// <summary>
    ///     This method converts a custom option entity into its REST representation.
    /// </summary>
    private static DhcpScopeCustomOptionDetail ToDetail(DhcpScopeCustomOptionEntity customOption) =>
        new(
            customOption.Id,
            customOption.ScopeId,
            customOption.OptionTag,
            customOption.ValueHex,
            customOption.IsDisabled,
            customOption.ValueSource,
            customOption.Comments,
            customOption.CreatedAtUtc,
            customOption.UpdatedAtUtc
        );
}
