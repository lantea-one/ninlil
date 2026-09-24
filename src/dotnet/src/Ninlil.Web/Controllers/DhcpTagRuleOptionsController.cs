using Microsoft.AspNetCore.Mvc;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Web.Examples.DhcpTagRuleOptions;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller administers the options a single tag rule contributes, nested under it
///     (<c>/api/dhcp-tag-rules/{tagRuleId}/options</c>) since an option only ever makes sense in
///     its owning rule's own context -- the same nesting <see cref="RecordsController" /> already
///     establishes for a zone's records. Every action requires the <c>dhcptagrules:read</c> or
///     <c>dhcptagrules:write</c> permission, the same as <see cref="DhcpTagRulesController" />.
/// </summary>
/// <param name="tagRuleRepository">The repository used to confirm the owning rule exists.</param>
/// <param name="tagRuleOptionRepository">The repository this controller reads and writes options through directly.</param>
/// <param name="unitOfWork">The unit of work every staged change is committed through.</param>
[ApiController]
[Route("api/dhcp-tag-rules/{tagRuleId:guid}/options")]
public class DhcpTagRuleOptionsController(
    IDhcpTagRuleRepository tagRuleRepository,
    IDhcpTagRuleOptionRepository tagRuleOptionRepository,
    IUnitOfWork unitOfWork
) : ControllerBase
{
    /// <summary>
    ///     This action lists every option a tag rule contributes.
    /// </summary>
    [HttpGet]
    [RequirePermission("dhcptagrules:read")]
    [SwaggerResponseExample(
        StatusCodes.Status200OK,
        typeof(DhcpTagRuleOptionDetailPagedResultExample)
    )]
    public async Task<ActionResult<PagedResult<DhcpTagRuleOptionDetail>>> List(
        Guid tagRuleId,
        [FromQuery] PaginationQuery pagination,
        CancellationToken stoppingToken
    )
    {
        if (await tagRuleRepository.GetByIdAsync(tagRuleId, stoppingToken) is null)
            throw new NotFoundException("DhcpTagRule", tagRuleId.ToString());

        (int skip, int take) = (pagination ?? new PaginationQuery()).Normalize();

        IReadOnlyList<DhcpTagRuleOptionEntity> options =
            await tagRuleOptionRepository.SearchByTagRuleIdAsync(
                tagRuleId,
                skip,
                take,
                stoppingToken
            );
        int totalCount = await tagRuleOptionRepository.CountByTagRuleIdAsync(
            tagRuleId,
            stoppingToken
        );

        return Ok(
            new PagedResult<DhcpTagRuleOptionDetail>(
                [.. options.Select(ToDetail)],
                skip,
                take,
                totalCount
            )
        );
    }

    /// <summary>
    ///     This action looks up a single option by its identifier.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("dhcptagrules:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(DhcpTagRuleOptionDetailExample))]
    public async Task<ActionResult<DhcpTagRuleOptionDetail>> Get(
        Guid tagRuleId,
        Guid id,
        CancellationToken stoppingToken
    )
    {
        DhcpTagRuleOptionEntity option = await tagRuleOptionRepository.GetByIdAsync(
            id,
            stoppingToken
        );
        if (option is null || option.TagRuleId != tagRuleId)
            throw new NotFoundException("DhcpTagRuleOption", id.ToString());

        return Ok(ToDetail(option));
    }

    /// <summary>
    ///     This action adds a new option to a tag rule.
    /// </summary>
    [HttpPost]
    [RequirePermission("dhcptagrules:write")]
    [SwaggerRequestExample(
        typeof(DhcpTagRuleOptionWriteRequest),
        typeof(DhcpTagRuleOptionWriteRequestExample)
    )]
    [SwaggerResponseExample(StatusCodes.Status201Created, typeof(DhcpTagRuleOptionDetailExample))]
    public async Task<ActionResult<DhcpTagRuleOptionDetail>> Create(
        Guid tagRuleId,
        DhcpTagRuleOptionWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        if (await tagRuleRepository.GetByIdAsync(tagRuleId, stoppingToken) is null)
            throw new NotFoundException("DhcpTagRule", tagRuleId.ToString());

        if (!TryValidate(request, out string normalizedValueHex, out string error))
            throw new ValidationException(error);

        DhcpTagRuleOptionEntity option = new()
        {
            Id = Guid.NewGuid(),
            TagRuleId = tagRuleId,
            OptionTag = request.OptionTag,
            ValueHex = normalizedValueHex,
            ValueSource = request.ValueSource,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        await tagRuleOptionRepository.AddAsync(option, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return CreatedAtAction(nameof(Get), new { tagRuleId, id = option.Id }, ToDetail(option));
    }

    /// <summary>
    ///     This action fully replaces an existing option's own fields, keeping its own identifier
    ///     and owning rule.
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("dhcptagrules:write")]
    [SwaggerRequestExample(
        typeof(DhcpTagRuleOptionWriteRequest),
        typeof(DhcpTagRuleOptionWriteRequestExample)
    )]
    public async Task<IActionResult> Replace(
        Guid tagRuleId,
        Guid id,
        DhcpTagRuleOptionWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        DhcpTagRuleOptionEntity option = await tagRuleOptionRepository.GetByIdAsync(
            id,
            stoppingToken
        );
        if (option is null || option.TagRuleId != tagRuleId)
            throw new NotFoundException("DhcpTagRuleOption", id.ToString());

        if (!TryValidate(request, out string normalizedValueHex, out string error))
            throw new ValidationException(error);

        option.OptionTag = request.OptionTag;
        option.ValueHex = normalizedValueHex;
        option.ValueSource = request.ValueSource;

        tagRuleOptionRepository.Update(option);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This action removes an option from the tag rule.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("dhcptagrules:write")]
    public async Task<IActionResult> Delete(
        Guid tagRuleId,
        Guid id,
        CancellationToken stoppingToken
    )
    {
        DhcpTagRuleOptionEntity option = await tagRuleOptionRepository.GetByIdAsync(
            id,
            stoppingToken
        );
        if (option is null || option.TagRuleId != tagRuleId)
            throw new NotFoundException("DhcpTagRuleOption", id.ToString());

        tagRuleOptionRepository.Remove(option);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This method validates a tag rule option write request, and canonicalizes
    ///     <see cref="DhcpTagRuleOptionWriteRequest.ValueHex" /> to the uppercase, even-length
    ///     hexadecimal form <see cref="Ninlil.Dhcp.Data.Abstractions.Entity.DhcpTagRuleOptionEntity.ValueHex" />
    ///     always stores, the same convention <see cref="DhcpScopeCustomOptionsController" />
    ///     already establishes for a scope's own custom options -- including that a blank
    ///     <c>valueHex</c> is only rejected for <see cref="DhcpOptionValueSourceEnum.Custom" />,
    ///     never for <see cref="DhcpOptionValueSourceEnum.ServerAddress" />, whose stored value the
    ///     delivery engine never reads at all.
    /// </summary>
    private static bool TryValidate(
        DhcpTagRuleOptionWriteRequest request,
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
    ///     This method converts a tag rule option entity into its REST representation.
    /// </summary>
    private static DhcpTagRuleOptionDetail ToDetail(DhcpTagRuleOptionEntity option) =>
        new(
            option.Id,
            option.TagRuleId,
            option.OptionTag,
            option.ValueHex,
            option.ValueSource,
            option.CreatedAtUtc
        );
}
