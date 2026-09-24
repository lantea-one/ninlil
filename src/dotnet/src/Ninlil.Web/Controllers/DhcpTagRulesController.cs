using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Web.Examples.DhcpTagRules;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller administers this server's own tag-based conditional option delivery rules
///     -- the top-level resource; a rule's own contributed options are managed separately,
///     through the nested <see cref="DhcpTagRuleOptionsController" />
///     (<c>/api/dhcp-tag-rules/{tagRuleId}/options</c>) -- the same "one parent, one nested child
///     collection" shape <see cref="DhcpScopesController" /> already establishes for a scope's own
///     reservations/custom options. Every action requires the <c>dhcptagrules:read</c> or
///     <c>dhcptagrules:write</c> permission, reused for the nested controller too. See
///     <see cref="Ninlil.Dhcp.Data.Abstractions.Entity.DhcpTagRuleEntity" />'s own doc comment for
///     the full match/priority/conflict-resolution semantics this resource's own fields drive.
/// </summary>
/// <param name="tagRuleRepository">The repository this controller reads and writes rules through.</param>
/// <param name="tagRuleOptionRepository">The repository used to remove a deleted rule's own options -- see <see cref="Delete" />.</param>
/// <param name="scopeRepository">The repository used to confirm an optional <c>scopeId</c> actually names a real scope.</param>
/// <param name="unitOfWork">The unit of work every staged change is committed through.</param>
[ApiController]
[Route("api/dhcp-tag-rules")]
public class DhcpTagRulesController(
    IDhcpTagRuleRepository tagRuleRepository,
    IDhcpTagRuleOptionRepository tagRuleOptionRepository,
    IDhcpScopeRepository scopeRepository,
    IUnitOfWork unitOfWork
) : ControllerBase
{
    /// <summary>
    ///     This action lists every tag rule this server manages.
    /// </summary>
    [HttpGet]
    [RequirePermission("dhcptagrules:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(DhcpTagRuleDetailPagedResultExample))]
    public async Task<ActionResult<PagedResult<DhcpTagRuleDetail>>> List(
        [FromQuery] PaginationQuery pagination,
        CancellationToken stoppingToken
    )
    {
        (int skip, int take) = (pagination ?? new PaginationQuery()).Normalize();

        IReadOnlyList<DhcpTagRuleEntity> rules = await tagRuleRepository.SearchAsync(
            skip,
            take,
            stoppingToken
        );
        int totalCount = await tagRuleRepository.CountAsync(stoppingToken);

        return Ok(
            new PagedResult<DhcpTagRuleDetail>([.. rules.Select(ToDetail)], skip, take, totalCount)
        );
    }

    /// <summary>
    ///     This action looks up a single tag rule by its identifier.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("dhcptagrules:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(DhcpTagRuleDetailExample))]
    public async Task<ActionResult<DhcpTagRuleDetail>> Get(Guid id, CancellationToken stoppingToken)
    {
        DhcpTagRuleEntity rule = await tagRuleRepository.GetByIdAsync(id, stoppingToken);
        if (rule is null)
            throw new NotFoundException("DhcpTagRule", id.ToString());

        return Ok(ToDetail(rule));
    }

    /// <summary>
    ///     This action creates a new tag rule.
    /// </summary>
    [HttpPost]
    [RequirePermission("dhcptagrules:write")]
    [SwaggerRequestExample(typeof(DhcpTagRuleWriteRequest), typeof(DhcpTagRuleWriteRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status201Created, typeof(DhcpTagRuleDetailExample))]
    public async Task<ActionResult<DhcpTagRuleDetail>> Create(
        DhcpTagRuleWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        (bool isValid, string createError) = await TryValidateAsync(request, stoppingToken);
        if (!isValid)
            throw new ValidationException(createError);

        DhcpTagRuleEntity rule = new()
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            ScopeId = request.ScopeId,
            Priority = request.Priority,
            IsDisabled = request.IsDisabled,
            VendorClassIdentifierMatch = request.VendorClassIdentifierMatch,
            VendorClassIdentifierMatchMode = request.VendorClassIdentifierMatchMode,
            UserClassMatch = request.UserClassMatch,
            UserClassMatchMode = request.UserClassMatchMode,
            RelayAgentCircuitIdHex = NormalizeHexOrNull(request.RelayAgentCircuitIdHex),
            RelayAgentRemoteIdHex = NormalizeHexOrNull(request.RelayAgentRemoteIdHex),
            RelayInterfaceIdHex = NormalizeHexOrNull(request.RelayInterfaceIdHex),
            VendorClassEnterpriseNumber = request.VendorClassEnterpriseNumber,
            Comments = request.Comments,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        await tagRuleRepository.AddAsync(rule, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return CreatedAtAction(nameof(Get), new { id = rule.Id }, ToDetail(rule));
    }

    /// <summary>
    ///     This action fully replaces an existing tag rule's own fields.
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("dhcptagrules:write")]
    [SwaggerRequestExample(typeof(DhcpTagRuleWriteRequest), typeof(DhcpTagRuleWriteRequestExample))]
    public async Task<IActionResult> Replace(
        Guid id,
        DhcpTagRuleWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        DhcpTagRuleEntity rule = await tagRuleRepository.GetByIdAsync(id, stoppingToken);
        if (rule is null)
            throw new NotFoundException("DhcpTagRule", id.ToString());

        (bool isValid, string replaceError) = await TryValidateAsync(request, stoppingToken);
        if (!isValid)
            throw new ValidationException(replaceError);

        rule.Name = request.Name;
        rule.ScopeId = request.ScopeId;
        rule.Priority = request.Priority;
        rule.IsDisabled = request.IsDisabled;
        rule.VendorClassIdentifierMatch = request.VendorClassIdentifierMatch;
        rule.VendorClassIdentifierMatchMode = request.VendorClassIdentifierMatchMode;
        rule.UserClassMatch = request.UserClassMatch;
        rule.UserClassMatchMode = request.UserClassMatchMode;
        rule.RelayAgentCircuitIdHex = NormalizeHexOrNull(request.RelayAgentCircuitIdHex);
        rule.RelayAgentRemoteIdHex = NormalizeHexOrNull(request.RelayAgentRemoteIdHex);
        rule.RelayInterfaceIdHex = NormalizeHexOrNull(request.RelayInterfaceIdHex);
        rule.VendorClassEnterpriseNumber = request.VendorClassEnterpriseNumber;
        rule.Comments = request.Comments;
        rule.UpdatedAtUtc = DateTimeOffset.UtcNow;

        tagRuleRepository.Update(rule);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This action partially updates an existing tag rule via RFC 7396 JSON Merge Patch: a
    ///     field absent from the patch document is left untouched, and any other present field
    ///     overwrites it.
    /// </summary>
    [HttpPatch("{id:guid}")]
    [RequirePermission("dhcptagrules:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(DhcpTagRulePatchRequestExample))]
    public async Task<IActionResult> Patch(
        Guid id,
        [FromBody] JsonElement patch,
        CancellationToken stoppingToken
    )
    {
        DhcpTagRuleEntity rule = await tagRuleRepository.GetByIdAsync(id, stoppingToken);
        if (rule is null)
            throw new NotFoundException("DhcpTagRule", id.ToString());

        if (patch.TryGetMergePatchField("name", out JsonElement name))
        {
            if (string.IsNullOrWhiteSpace(name.GetString()))
                throw new ValidationException("'name' must not be empty.");

            rule.Name = name.GetString();
        }

        if (patch.TryGetMergePatchField("scopeId", out JsonElement scopeId))
        {
            Guid? newScopeId = scopeId.ValueKind == JsonValueKind.Null ? null : scopeId.GetGuid();

            if (
                newScopeId is not null
                && await scopeRepository.GetByIdAsync(newScopeId.Value, stoppingToken) is null
            )
                throw new ValidationException($"No DHCP scope with id '{newScopeId}' exists.");

            rule.ScopeId = newScopeId;
        }

        if (patch.TryGetMergePatchField("priority", out JsonElement priority))
            rule.Priority = priority.GetInt32();

        if (patch.TryGetMergePatchField("isDisabled", out JsonElement isDisabled))
            rule.IsDisabled = isDisabled.GetBoolean();

        if (
            patch.TryGetMergePatchField(
                "vendorClassIdentifierMatch",
                out JsonElement vendorClassIdentifierMatch
            )
        )
            rule.VendorClassIdentifierMatch =
                vendorClassIdentifierMatch.ValueKind == JsonValueKind.Null
                    ? null
                    : vendorClassIdentifierMatch.GetString();

        if (
            patch.TryGetMergePatchField(
                "vendorClassIdentifierMatchMode",
                out JsonElement vendorClassIdentifierMatchMode
            )
        )
            rule.VendorClassIdentifierMatchMode = ParseMatchMode(
                vendorClassIdentifierMatchMode,
                "vendorClassIdentifierMatchMode"
            );

        if (patch.TryGetMergePatchField("userClassMatch", out JsonElement userClassMatch))
            rule.UserClassMatch =
                userClassMatch.ValueKind == JsonValueKind.Null ? null : userClassMatch.GetString();

        if (patch.TryGetMergePatchField("userClassMatchMode", out JsonElement userClassMatchMode))
            rule.UserClassMatchMode = ParseMatchMode(userClassMatchMode, "userClassMatchMode");

        if (
            patch.TryGetMergePatchField(
                "relayAgentCircuitIdHex",
                out JsonElement relayAgentCircuitIdHex
            )
        )
            rule.RelayAgentCircuitIdHex = NormalizeHexFieldOrThrow(
                relayAgentCircuitIdHex,
                "relayAgentCircuitIdHex"
            );

        if (
            patch.TryGetMergePatchField(
                "relayAgentRemoteIdHex",
                out JsonElement relayAgentRemoteIdHex
            )
        )
            rule.RelayAgentRemoteIdHex = NormalizeHexFieldOrThrow(
                relayAgentRemoteIdHex,
                "relayAgentRemoteIdHex"
            );

        if (patch.TryGetMergePatchField("relayInterfaceIdHex", out JsonElement relayInterfaceIdHex))
            rule.RelayInterfaceIdHex = NormalizeHexFieldOrThrow(
                relayInterfaceIdHex,
                "relayInterfaceIdHex"
            );

        if (
            patch.TryGetMergePatchField(
                "vendorClassEnterpriseNumber",
                out JsonElement vendorClassEnterpriseNumber
            )
        )
            rule.VendorClassEnterpriseNumber =
                vendorClassEnterpriseNumber.ValueKind == JsonValueKind.Null
                    ? null
                    : vendorClassEnterpriseNumber.GetUInt32();

        if (patch.TryGetMergePatchField("comments", out JsonElement comments))
            rule.Comments = comments.ValueKind == JsonValueKind.Null ? null : comments.GetString();

        rule.UpdatedAtUtc = DateTimeOffset.UtcNow;
        tagRuleRepository.Update(rule);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This action permanently deletes a tag rule and every option it contributes -- removed
    ///     explicitly, rather than relying on the relational backend's own cascade delete, since
    ///     MongoDB has no equivalent mechanism at all, the same reasoning
    ///     <see cref="ClientGroupsController.Delete" /> already applies to a deleted client
    ///     group's own dependent rows.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("dhcptagrules:write")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken stoppingToken)
    {
        DhcpTagRuleEntity rule = await tagRuleRepository.GetByIdAsync(id, stoppingToken);
        if (rule is null)
            throw new NotFoundException("DhcpTagRule", id.ToString());

        IReadOnlyList<DhcpTagRuleOptionEntity> options = await tagRuleOptionRepository.ListAsync(
            option => option.TagRuleId == id,
            stoppingToken
        );
        foreach (DhcpTagRuleOptionEntity option in options)
            tagRuleOptionRepository.Remove(option);

        tagRuleRepository.Remove(rule);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This method parses a merge-patch match-mode field, rejecting anything that isn't a
    ///     recognized <see cref="Dhcpv4TagMatchModeEnum" /> member name.
    /// </summary>
    private static Dhcpv4TagMatchModeEnum ParseMatchMode(JsonElement element, string fieldName)
    {
        if (
            !Enum.TryParse(
                element.GetString(),
                ignoreCase: true,
                out Dhcpv4TagMatchModeEnum matchMode
            )
        )
            throw new ValidationException(
                $"'{fieldName}' must be one of: "
                    + string.Join(", ", Enum.GetNames<Dhcpv4TagMatchModeEnum>())
            );

        return matchMode;
    }

    /// <summary>
    ///     This method reads and validates a merge-patch hex-value field, allowing an explicit
    ///     <see langword="null" /> to clear it.
    /// </summary>
    private static string NormalizeHexFieldOrThrow(JsonElement element, string fieldName)
    {
        if (element.ValueKind == JsonValueKind.Null)
            return null;

        string value = element.GetString();
        if (string.IsNullOrEmpty(value))
            return null;

        try
        {
            return Convert.ToHexString(Convert.FromHexString(value));
        }
        catch (FormatException)
        {
            throw new ValidationException(
                $"'{fieldName}' must be a valid hexadecimal string with an even number of "
                    + "characters, using only 0-9 and A-F."
            );
        }
    }

    /// <summary>
    ///     This method canonicalizes an optional hex string field to its uppercase form, or
    ///     returns <see langword="null" /> unchanged.
    /// </summary>
    private static string NormalizeHexOrNull(string value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        return Convert.ToHexString(Convert.FromHexString(value));
    }

    /// <summary>
    ///     This method validates every field of a tag rule write request up front, so a
    ///     <c>POST</c>/<c>PUT</c> either fully succeeds or is rejected outright with no partial
    ///     effect. When <see cref="DhcpTagRuleWriteRequest.ScopeId" /> is set, its own existence is
    ///     checked here too, treated as an invalid value on the rule being written rather than a
    ///     lookup of the scope itself -- the same "referenced entity absence is a 400, not a 404"
    ///     precedent <see cref="ClientGroupListAssignmentsController.Create" /> already establishes
    ///     for a list assignment's own referenced list.
    /// </summary>
    private async Task<(bool IsValid, string Error)> TryValidateAsync(
        DhcpTagRuleWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Name))
            return (false, "'name' is required.");

        if (
            request.ScopeId is not null
            && await scopeRepository.GetByIdAsync(request.ScopeId.Value, stoppingToken) is null
        )
            return (false, $"No DHCP scope with id '{request.ScopeId}' exists.");

        foreach (
            (string value, string fieldName) in new[]
            {
                (request.RelayAgentCircuitIdHex, "relayAgentCircuitIdHex"),
                (request.RelayAgentRemoteIdHex, "relayAgentRemoteIdHex"),
                (request.RelayInterfaceIdHex, "relayInterfaceIdHex"),
            }
        )
        {
            if (string.IsNullOrEmpty(value))
                continue;

            try
            {
                Convert.FromHexString(value);
            }
            catch (FormatException)
            {
                return (
                    false,
                    $"'{fieldName}' must be a valid hexadecimal string with an even number of "
                        + "characters, using only 0-9 and A-F."
                );
            }
        }

        return (true, null);
    }

    /// <summary>
    ///     This method converts a tag rule entity into its REST representation.
    /// </summary>
    private static DhcpTagRuleDetail ToDetail(DhcpTagRuleEntity rule) =>
        new(
            rule.Id,
            rule.Name,
            rule.ScopeId,
            rule.Priority,
            rule.IsDisabled,
            rule.VendorClassIdentifierMatch,
            rule.VendorClassIdentifierMatchMode,
            rule.UserClassMatch,
            rule.UserClassMatchMode,
            rule.RelayAgentCircuitIdHex,
            rule.RelayAgentRemoteIdHex,
            rule.RelayInterfaceIdHex,
            rule.VendorClassEnterpriseNumber,
            rule.Comments,
            rule.CreatedAtUtc,
            rule.UpdatedAtUtc
        );
}
