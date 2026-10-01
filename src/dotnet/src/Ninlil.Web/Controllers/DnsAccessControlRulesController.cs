using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Web.Examples.AccessControlRules;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller manages the access-control rules
///     (<see cref="DnsAccessControlRuleEntity" />): each grants a network a set of capabilities.
///     <c>DnsAccessControlRuleCache</c> and <c>DnsAccessControlEngine</c> in
///     <c>Ninlil.Dns.Server.AccessControl</c> check them in the recursion gate of
///     <c>RecursiveQueryResolver.TryResolveAsync</c> and the rate-limit bypass of
///     <c>TokenBucketDnsRateLimiter.TryAcquireAsync</c>. A rule is one row per network with no
///     parent, so the route is flat (<c>/api/access-control-rules</c>), like
///     <see cref="ForwardersController" />, unlike the group-plus-networks shape of
///     <see cref="ClientGroupsController" /> and <see cref="ClientGroupNetworksController" />.
///     Reads need <c>accesscontrol:read</c> and changes need <c>accesscontrol:write</c>.
///     <para>
///         <b>Every create, update and delete publishes a
///         <see cref="DnsAccessControlRuleChangedEvent" /></b> through
///         <see cref="IDnsServerEventPublisher" />. That event is what reloads
///         <c>DnsAccessControlRuleCache</c>; a write without it would not affect query answering
///         until a restart.
///     </para>
///     <para>
///         <b>Capabilities travel as an array of names.</b>
///         <see cref="DnsAccessControlCapabilityEnum" /> is a flags enum and has no
///         <c>[JsonConverter&lt;EnumDescriptionJsonConverter&lt;T&gt;&gt;]</c>, since that
///         converter handles one <see cref="System.Runtime.Serialization.EnumMemberAttribute" />
///         name, not a combined value. This controller converts between the name array of
///         <see cref="DnsAccessControlRuleWriteRequest.Capabilities" /> and
///         <see cref="DnsAccessControlRuleDetail.Capabilities" /> and the stored bitmask with
///         <see cref="TryParseCapabilities" /> and <see cref="ToCapabilityList" />.
///     </para>
/// </summary>
/// <param name="ruleRepository">Reads and writes rules.</param>
/// <param name="unitOfWork">Commits staged changes.</param>
/// <param name="eventPublisher">
///     Publishes <see cref="DnsAccessControlRuleChangedEvent" /> on every write, so
///     <c>DnsAccessControlRuleCache</c> reloads and
///     <c>AccessControlRuleClusterSyncEventSubscriber</c> sends the change to the other cluster
///     nodes.
/// </param>
/// <param name="clusterOptions">
///     Supplies this node's cluster node id, stamped onto
///     <c>DnsAccessControlRuleEntity.OriginatingNodeId</c> on every create and update, whether or
///     not clustering is enabled.
/// </param>
[ApiController]
[Route("api/access-control-rules")]
public class DnsAccessControlRulesController(
    IDnsAccessControlRuleRepository ruleRepository,
    IUnitOfWork unitOfWork,
    IDnsServerEventPublisher eventPublisher,
    IOptionsMonitor<ClusterOptions> clusterOptions
) : ControllerBase
{
    /// <summary>
    ///     This action lists access-control rules, enabled or not, one page at a time. Requires
    ///     <c>accesscontrol:read</c>.
    /// </summary>
    [HttpGet]
    [RequirePermission("accesscontrol:read")]
    [SwaggerResponseExample(
        StatusCodes.Status200OK,
        typeof(DnsAccessControlRuleDetailPagedResultExample)
    )]
    public async Task<ActionResult<PagedResult<DnsAccessControlRuleDetail>>> List(
        [FromQuery] PaginationQuery pagination,
        CancellationToken stoppingToken
    )
    {
        (int skip, int take) = (pagination ?? new PaginationQuery()).Normalize();

        // The page and the total count are both computed by the storage engine (ORDER
        // BY/OFFSET/LIMIT or the MongoDB equivalent), not by loading every rule into memory.
        IReadOnlyList<DnsAccessControlRuleEntity> rules = await ruleRepository.SearchAsync(
            skip,
            take,
            stoppingToken
        );
        int totalCount = await ruleRepository.CountAsync(stoppingToken);

        return Ok(
            new PagedResult<DnsAccessControlRuleDetail>(
                [.. rules.Select(ToDetail)],
                skip,
                take,
                totalCount
            )
        );
    }

    /// <summary>
    ///     This action returns one access-control rule. Returns 404 for an unknown id. Requires
    ///     <c>accesscontrol:read</c>.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("accesscontrol:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(DnsAccessControlRuleDetailExample))]
    public async Task<ActionResult<DnsAccessControlRuleDetail>> Get(
        Guid id,
        CancellationToken stoppingToken
    )
    {
        DnsAccessControlRuleEntity rule = await ruleRepository.GetByIdAsync(id, stoppingToken);
        if (rule is null)
            throw new NotFoundException("AccessControlRule", id.ToString());

        return Ok(ToDetail(rule));
    }

    /// <summary>
    ///     This action creates an access-control rule; it applies to queries without a restart.
    ///     Returns 400 for a missing name, an invalid network or an unknown capability name.
    ///     Requires <c>accesscontrol:write</c>.
    /// </summary>
    [HttpPost]
    [RequirePermission("accesscontrol:write")]
    [SwaggerRequestExample(
        typeof(DnsAccessControlRuleWriteRequest),
        typeof(DnsAccessControlRuleWriteRequestExample)
    )]
    [SwaggerResponseExample(
        StatusCodes.Status201Created,
        typeof(DnsAccessControlRuleDetailExample)
    )]
    public async Task<ActionResult<DnsAccessControlRuleDetail>> Create(
        DnsAccessControlRuleWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        if (string.IsNullOrWhiteSpace(request?.Name))
            throw new ValidationException("'name' is required.");

        if (!TryValidateCidr(request.Cidr, out string cidrError))
            throw new ValidationException(cidrError);

        if (
            !TryParseCapabilities(
                request.Capabilities,
                out DnsAccessControlCapabilityEnum capabilities,
                out string capabilitiesError
            )
        )
            throw new ValidationException(capabilitiesError);

        DnsAccessControlRuleEntity rule = new()
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Cidr = request.Cidr,
            Capabilities = capabilities,
            IsDisabled = request.IsDisabled,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            OriginatingNodeId = clusterOptions.CurrentValue.NodeId,
        };

        await ruleRepository.AddAsync(rule, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        eventPublisher.Publish(
            new DnsAccessControlRuleChangedEvent(DateTimeOffset.UtcNow, rule.Id)
        );

        return CreatedAtAction(nameof(Get), new { id = rule.Id }, ToDetail(rule));
    }

    /// <summary>
    ///     This action replaces an access-control rule's fields; the change applies without a
    ///     restart. Validation and errors are as for <see cref="Create" />, plus 404 for an unknown
    ///     id. Requires <c>accesscontrol:write</c>.
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("accesscontrol:write")]
    [SwaggerRequestExample(
        typeof(DnsAccessControlRuleWriteRequest),
        typeof(DnsAccessControlRuleWriteRequestExample)
    )]
    public async Task<IActionResult> Replace(
        Guid id,
        DnsAccessControlRuleWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        DnsAccessControlRuleEntity rule = await ruleRepository.GetByIdAsync(id, stoppingToken);
        if (rule is null)
            throw new NotFoundException("AccessControlRule", id.ToString());

        if (string.IsNullOrWhiteSpace(request?.Name))
            throw new ValidationException("'name' is required.");

        if (!TryValidateCidr(request.Cidr, out string cidrError))
            throw new ValidationException(cidrError);

        if (
            !TryParseCapabilities(
                request.Capabilities,
                out DnsAccessControlCapabilityEnum capabilities,
                out string capabilitiesError
            )
        )
            throw new ValidationException(capabilitiesError);

        rule.Name = request.Name;
        rule.Cidr = request.Cidr;
        rule.Capabilities = capabilities;
        rule.IsDisabled = request.IsDisabled;
        rule.UpdatedAtUtc = DateTimeOffset.UtcNow;
        rule.OriginatingNodeId = clusterOptions.CurrentValue.NodeId;

        ruleRepository.Update(rule);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        eventPublisher.Publish(
            new DnsAccessControlRuleChangedEvent(DateTimeOffset.UtcNow, rule.Id)
        );

        return NoContent();
    }

    /// <summary>
    ///     This action deletes an access-control rule, so it no longer grants anything. Returns 404
    ///     for an unknown id. Requires <c>accesscontrol:write</c>.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("accesscontrol:write")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken stoppingToken)
    {
        DnsAccessControlRuleEntity rule = await ruleRepository.GetByIdAsync(id, stoppingToken);
        if (rule is null)
            throw new NotFoundException("AccessControlRule", id.ToString());

        ruleRepository.Remove(rule);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        eventPublisher.Publish(
            new DnsAccessControlRuleChangedEvent(DateTimeOffset.UtcNow, id, IsDeleted: true)
        );

        return NoContent();
    }

    /// <summary>
    ///     This method checks that a CIDR value parses with <see cref="System.Net.IPNetwork" />,
    ///     the parser <c>ClientGroupNetworksController.TryValidateCidr</c> uses and
    ///     <c>DnsAccessControlRuleCache</c> matches with, so an accepted rule can match queries.
    /// </summary>
    private static bool TryValidateCidr(string cidr, out string error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(cidr))
        {
            error = "'cidr' is required.";
            return false;
        }

        if (!System.Net.IPNetwork.TryParse(cidr, out _))
        {
            error = $"'{cidr}' is not a valid IP address or CIDR network.";
            return false;
        }

        return true;
    }

    /// <summary>
    ///     This method converts an array of capability names into the stored
    ///     <see cref="DnsAccessControlCapabilityEnum" /> bitmask. Each entry must name one defined
    ///     member other than <see cref="DnsAccessControlCapabilityEnum.None" />, case-sensitively;
    ///     <see cref="Enum.IsDefined{TEnum}(TEnum)" /> rejects a combined value. An unknown name is
    ///     a validation error, not ignored. A null list means no capabilities.
    /// </summary>
    private static bool TryParseCapabilities(
        IReadOnlyList<string> names,
        out DnsAccessControlCapabilityEnum capabilities,
        out string error
    )
    {
        capabilities = DnsAccessControlCapabilityEnum.None;
        error = null;

        if (names is null)
            return true;

        foreach (string name in names)
        {
            if (
                !Enum.TryParse(name, ignoreCase: false, out DnsAccessControlCapabilityEnum parsed)
                || parsed == DnsAccessControlCapabilityEnum.None
                || !Enum.IsDefined(parsed)
            )
            {
                error =
                    $"'{name}' is not a recognized access-control capability. Valid values: "
                    + string.Join(
                        ", ",
                        Enum.GetValues<DnsAccessControlCapabilityEnum>()
                            .Where(value => value != DnsAccessControlCapabilityEnum.None)
                    )
                    + ".";
                return false;
            }

            capabilities |= parsed;
        }

        return true;
    }

    /// <summary>
    ///     This method turns the stored <see cref="DnsAccessControlCapabilityEnum" /> bitmask into
    ///     the array of names the API returns, the inverse of <see cref="TryParseCapabilities" />.
    /// </summary>
    private static IReadOnlyList<string> ToCapabilityList(
        DnsAccessControlCapabilityEnum capabilities
    ) =>
        [
            .. Enum.GetValues<DnsAccessControlCapabilityEnum>()
                .Where(value => value != DnsAccessControlCapabilityEnum.None)
                .Where(value => capabilities.HasFlag(value))
                .Select(value => value.ToString()),
        ];

    /// <summary>
    ///     This method converts a rule entity to its API representation.
    /// </summary>
    private static DnsAccessControlRuleDetail ToDetail(DnsAccessControlRuleEntity rule) =>
        new(
            rule.Id,
            rule.Name,
            rule.Cidr,
            ToCapabilityList(rule.Capabilities),
            rule.IsDisabled,
            rule.CreatedAtUtc,
            rule.UpdatedAtUtc,
            rule.OriginatingNodeId
        );
}
