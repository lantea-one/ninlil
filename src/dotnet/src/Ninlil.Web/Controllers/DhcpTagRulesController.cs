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
using Ninlil.Web.Routing;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller manages tag rules for conditional DHCP option delivery: a rule matches
///     clients by vendor class, user class, relay agent IDs or enterprise number, optionally
///     within one scope, and contributes options to them. A rule's options are managed through the
///     nested <see cref="DhcpTagRuleOptionsController" />
///     (<c>/api/dhcp-tag-rules/{tagRuleId}/options</c>), which uses the same permissions:
///     <c>dhcptagrules:read</c> for reads and <c>dhcptagrules:write</c> for changes. See
///     <see cref="Ninlil.Dhcp.Data.Abstractions.Entity.DhcpTagRuleEntity" /> for how matching,
///     priority and conflicts work.
/// </summary>
/// <param name="tagRuleRepository">Reads and writes rules.</param>
/// <param name="tagRuleOptionRepository">Removes a deleted rule's options (see <see cref="Delete" />).</param>
/// <param name="scopeRepository">Confirms an optional <c>scopeId</c> names an existing scope.</param>
/// <param name="unitOfWork">Commits staged changes.</param>
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
    ///     This action lists tag rules, one page at a time. Requires <c>dhcptagrules:read</c>.
    /// </summary>
    [HttpGet]
    [ReadFromReplica]
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
    ///     This action returns one tag rule. Returns 404 for an unknown id. Requires
    ///     <c>dhcptagrules:read</c>.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ReadFromReplica]
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
    ///     This action creates a tag rule. Relay ID hex values are stored in uppercase. Returns 400
    ///     for a missing name, an unknown scope or a relay ID that is not even-length hex. Requires
    ///     <c>dhcptagrules:write</c>.
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
    ///     This action replaces a tag rule's fields. Validation is as for <see cref="Create" />;
    ///     returns 404 for an unknown id. Requires <c>dhcptagrules:write</c>.
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
    ///     This action updates a tag rule as an RFC 7396 merge patch: an absent field is unchanged,
    ///     a JSON <c>null</c> clears a nullable field, and any other value replaces it. Returns 400
    ///     for an empty name, an unknown scope, an unknown match mode or bad hex, and 404 for an
    ///     unknown id. Requires <c>dhcptagrules:write</c>.
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
    ///     This action deletes a tag rule and every option it contributes, removed explicitly
    ///     rather than by a relational cascade delete, which MongoDB lacks. Returns 404 for an
    ///     unknown id. Requires <c>dhcptagrules:write</c>.
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
    ///     This method parses a match-mode field, rejecting anything that is not a
    ///     <see cref="Dhcpv4TagMatchModeEnum" /> member name.
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
    ///     This method checks a merge-patch hex field and converts it to uppercase; a JSON
    ///     <see langword="null" /> or empty value clears it.
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
    ///     This method converts an optional hex string to uppercase, or returns
    ///     <see langword="null" /> for an empty one. The value must already be valid hex.
    /// </summary>
    private static string NormalizeHexOrNull(string value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        return Convert.ToHexString(Convert.FromHexString(value));
    }

    /// <summary>
    ///     This method checks every field of a tag rule write request before anything is saved, so
    ///     a POST or PUT either succeeds fully or changes nothing. A
    ///     <see cref="DhcpTagRuleWriteRequest.ScopeId" /> that names no scope is an invalid value
    ///     in the request (400), not a missing resource, as in
    ///     <see cref="ClientGroupListAssignmentsController.Create" />.
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
    ///     This method converts a tag rule entity to its API representation.
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
