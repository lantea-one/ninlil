using Microsoft.AspNetCore.Mvc;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Web.Examples.DhcpScopeCustomOptions;
using Ninlil.Web.Models;
using Ninlil.Web.Routing;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller manages the raw DHCPv4 options (tag and value) of one DHCP scope, nested
///     under it at <c>/api/dhcp-scopes/{scopeId}/custom-options</c>. Only the DHCPv4 engine
///     delivers them (see
///     <see cref="Ninlil.Dhcp.Data.Abstractions.Entity.DhcpScopeCustomOptionEntity" /> for
///     precedence over the standard options and the Parameter Request List). Reads need
///     <c>dhcpscopes:read</c> and changes need <c>dhcpscopes:write</c>, as for
///     <see cref="DhcpScopesController" />.
/// </summary>
/// <param name="scopeRepository">Confirms the scope exists.</param>
/// <param name="customOptionRepository">Reads and writes custom options.</param>
/// <param name="unitOfWork">Commits staged changes.</param>
[ApiController]
[Route("api/dhcp-scopes/{scopeId:guid}/custom-options")]
public class DhcpScopeCustomOptionsController(
    IDhcpScopeRepository scopeRepository,
    IDhcpScopeCustomOptionRepository customOptionRepository,
    IUnitOfWork unitOfWork
) : ControllerBase
{
    /// <summary>
    ///     This action lists a scope's custom options, one page at a time. Returns 404 for an
    ///     unknown scope. Requires <c>dhcpscopes:read</c>.
    /// </summary>
    [HttpGet]
    [ReadFromReplica]
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
    ///     This action returns one custom option. Returns 404 when it does not exist or belongs to
    ///     another scope. Requires <c>dhcpscopes:read</c>.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ReadFromReplica]
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
    ///     This action adds a custom option to a scope. The value is stored as uppercase hex.
    ///     Returns 400 for a value that is not even-length hex, or a missing value when the source
    ///     is <c>Custom</c>, for a DHCPv4 scope a reserved tag (0, 52, 53, 82, 255) or a value over
    ///     255 bytes, and 404 for an unknown scope. Requires <c>dhcpscopes:write</c>.
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
        DhcpScopeEntity scope = await scopeRepository.GetByIdAsync(scopeId, stoppingToken);
        if (scope is null)
            throw new NotFoundException("DhcpScope", scopeId.ToString());

        if (!TryValidate(request, scope, out string normalizedValueHex, out string error))
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
    ///     This action replaces a custom option's fields, keeping its id and scope. Validation is
    ///     as for <see cref="Create" />; returns 404 when the option does not exist or belongs to
    ///     another scope. Requires <c>dhcpscopes:write</c>.
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

        DhcpScopeEntity scope = await scopeRepository.GetByIdAsync(scopeId, stoppingToken);
        if (scope is null)
            throw new NotFoundException("DhcpScope", scopeId.ToString());

        if (!TryValidate(request, scope, out string normalizedValueHex, out string error))
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
    ///     This action removes a custom option from the scope. Returns 404 when it does not exist
    ///     or belongs to another scope. Requires <c>dhcpscopes:write</c>.
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
    ///     This method checks a custom option request and converts
    ///     <see cref="DhcpScopeCustomOptionWriteRequest.ValueHex" /> to the uppercase, even-length
    ///     hex that
    ///     <see cref="Ninlil.Dhcp.Data.Abstractions.Entity.DhcpScopeCustomOptionEntity.ValueHex" />
    ///     stores. A blank value is an error only for
    ///     <see cref="DhcpOptionValueSourceEnum.Custom" />, where the stored value is what gets
    ///     sent. For <see cref="DhcpOptionValueSourceEnum.ServerAddress" /> the server's address is
    ///     sent instead and the value is never read, so a blank value is stored as an empty string.
    ///     For a DHCPv4 <paramref name="scope" /> the tag and value length must also pass
    ///     <see cref="DhcpRawOptionRules.TryValidateDhcpv4" /> (a server address is 4 bytes).
    /// </summary>
    private static bool TryValidate(
        DhcpScopeCustomOptionWriteRequest request,
        DhcpScopeEntity scope,
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
                return TryValidateRawOption(request.OptionTag, 4, scope, out error);
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

        return TryValidateRawOption(
            request.OptionTag,
            normalizedValueHex.Length / 2,
            scope,
            out error
        );
    }

    /// <summary>
    ///     This method applies <see cref="DhcpRawOptionRules.TryValidateDhcpv4" /> to an option of
    ///     a DHCPv4 scope. Only the DHCPv4 engine delivers custom options, so a DHCPv6 scope is
    ///     not checked.
    /// </summary>
    private static bool TryValidateRawOption(
        byte tag,
        int valueLength,
        DhcpScopeEntity scope,
        out string error
    )
    {
        error = null;

        return scope.ProtocolVersion != InternetProtocolVersionEnum.IPv4
            || DhcpRawOptionRules.TryValidateDhcpv4(tag, valueLength, out error);
    }

    /// <summary>
    ///     This method converts a custom option entity to its API representation.
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
