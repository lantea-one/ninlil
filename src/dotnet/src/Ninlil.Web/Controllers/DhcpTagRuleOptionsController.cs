using Microsoft.AspNetCore.Mvc;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Web.Examples.DhcpTagRuleOptions;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller manages the options one tag rule contributes, nested under it at
///     <c>/api/dhcp-tag-rules/{tagRuleId}/options</c>. Reads need <c>dhcptagrules:read</c> and
///     changes need <c>dhcptagrules:write</c>, as for <see cref="DhcpTagRulesController" />.
/// </summary>
/// <param name="tagRuleRepository">Confirms the rule exists.</param>
/// <param name="scopeRepository">Finds the protocol of the rule's scope.</param>
/// <param name="tagRuleOptionRepository">Reads and writes options.</param>
/// <param name="unitOfWork">Commits staged changes.</param>
[ApiController]
[Route("api/dhcp-tag-rules/{tagRuleId:guid}/options")]
public class DhcpTagRuleOptionsController(
    IDhcpTagRuleRepository tagRuleRepository,
    IDhcpScopeRepository scopeRepository,
    IDhcpTagRuleOptionRepository tagRuleOptionRepository,
    IUnitOfWork unitOfWork
) : ControllerBase
{
    /// <summary>
    ///     This action lists a rule's options, one page at a time. Returns 404 for an unknown rule.
    ///     Requires <c>dhcptagrules:read</c>.
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
    ///     This action returns one option. Returns 404 when it does not exist or belongs to another
    ///     rule. Requires <c>dhcptagrules:read</c>.
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
    ///     This action adds an option to a tag rule. The value is stored as uppercase hex. Returns
    ///     400 for a value that is not even-length hex, or a missing value when the source is
    ///     <c>Custom</c>, for a global or DHCPv4-scope rule a reserved tag (0, 52, 53, 82, 255) or
    ///     a value over 255 bytes, for a DHCPv6-scope rule tag 0 or a value over 65 535 bytes, and
    ///     404 for an unknown rule. Requires <c>dhcptagrules:write</c>.
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
        DhcpTagRuleEntity tagRule = await tagRuleRepository.GetByIdAsync(tagRuleId, stoppingToken);
        if (tagRule is null)
            throw new NotFoundException("DhcpTagRule", tagRuleId.ToString());

        bool isDhcpv6 = await IsDhcpv6RuleAsync(tagRule, stoppingToken);
        if (!TryValidate(request, isDhcpv6, out string normalizedValueHex, out string error))
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
    ///     This action replaces an option's fields, keeping its id and rule. Validation is as for
    ///     <see cref="Create" />; returns 404 when the option does not exist or belongs to another
    ///     rule. Requires <c>dhcptagrules:write</c>.
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

        DhcpTagRuleEntity tagRule = await tagRuleRepository.GetByIdAsync(tagRuleId, stoppingToken);
        if (tagRule is null)
            throw new NotFoundException("DhcpTagRule", tagRuleId.ToString());

        bool isDhcpv6 = await IsDhcpv6RuleAsync(tagRule, stoppingToken);
        if (!TryValidate(request, isDhcpv6, out string normalizedValueHex, out string error))
            throw new ValidationException(error);

        option.OptionTag = request.OptionTag;
        option.ValueHex = normalizedValueHex;
        option.ValueSource = request.ValueSource;

        tagRuleOptionRepository.Update(option);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This action removes an option from the rule. Returns 404 when it does not exist or
    ///     belongs to another rule. Requires <c>dhcptagrules:write</c>.
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
    ///     This method reports whether a rule is limited to a DHCPv6 scope. A global rule, or one
    ///     for a DHCPv4 scope, feeds the DHCPv4 engine, so it gets the stricter DHCPv4 limits.
    /// </summary>
    private async Task<bool> IsDhcpv6RuleAsync(
        DhcpTagRuleEntity tagRule,
        CancellationToken stoppingToken
    )
    {
        if (tagRule.ScopeId is not { } scopeId)
            return false;

        DhcpScopeEntity scope = await scopeRepository.GetByIdAsync(scopeId, stoppingToken);

        return scope?.ProtocolVersion == InternetProtocolVersionEnum.IPv6;
    }

    /// <summary>
    ///     This method checks an option request and converts
    ///     <see cref="DhcpTagRuleOptionWriteRequest.ValueHex" /> to the uppercase, even-length hex
    ///     that
    ///     <see cref="Ninlil.Dhcp.Data.Abstractions.Entity.DhcpTagRuleOptionEntity.ValueHex" />
    ///     stores, with the same rule as <see cref="DhcpScopeCustomOptionsController" />: a blank
    ///     value is an error only for <see cref="DhcpOptionValueSourceEnum.Custom" />, since for
    ///     <see cref="DhcpOptionValueSourceEnum.ServerAddress" /> the value is never read. The tag
    ///     and value length must also pass <see cref="DhcpRawOptionRules.TryValidateDhcpv6" /> for
    ///     a DHCPv6 rule (<paramref name="isDhcpv6" />) and
    ///     <see cref="DhcpRawOptionRules.TryValidateDhcpv4" /> otherwise. A server address is 4
    ///     bytes for DHCPv4 and 16 for DHCPv6.
    /// </summary>
    private static bool TryValidate(
        DhcpTagRuleOptionWriteRequest request,
        bool isDhcpv6,
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
                return TryValidateRawOption(
                    request.OptionTag,
                    isDhcpv6 ? 16 : 4,
                    isDhcpv6,
                    out error
                );
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
            isDhcpv6,
            out error
        );
    }

    /// <summary>
    ///     This method applies the DHCPv6 or the DHCPv4 option rules to one option.
    /// </summary>
    private static bool TryValidateRawOption(
        byte tag,
        int valueLength,
        bool isDhcpv6,
        out string error
    ) =>
        isDhcpv6
            ? DhcpRawOptionRules.TryValidateDhcpv6(tag, valueLength, out error)
            : DhcpRawOptionRules.TryValidateDhcpv4(tag, valueLength, out error);

    /// <summary>
    ///     This method converts an option entity to its API representation.
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
