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
///     This controller administers the real per-client/network access-control rule set
///     -- <see cref="DnsAccessControlRuleEntity" />, the flat, top-level entity
///     <c>DnsAccessControlRuleCache</c>/<c>DnsAccessControlEngine</c> (in the dedicated
///     <c>Ninlil.Dns.Server.AccessControl</c> project) consult from two real hot-path call
///     sites: <c>RecursiveQueryResolver.TryResolveAsync</c>'s own recursion gate, and
///     <c>TokenBucketDnsRateLimiter.TryAcquireAsync</c>'s own rate-limit-bypass check. This is a
///     flat, non-nested resource with no owning parent -- one row per network, deliberately not
///     the two-table "named group + separate networks" shape <see cref="ClientGroupsController" />/
///     <see cref="ClientGroupNetworksController" /> use for filter-list client-group scoping -- so this
///     controller's own REST shape follows <see cref="ForwardersController" />'s flat,
///     top-level route convention (<c>/api/access-control-rules</c>, no parent segment) rather
///     than <see cref="ClientGroupNetworksController" />'s nested-under-a-group one; the latter is
///     only this feature's closest precedent for its own Angular list-plus-form page layout and
///     interaction pattern (a named, network-scoped rule list), not its route nesting, which has
///     no analogue here since there is no owning group. Every action requires the
///     <c>accesscontrol:read</c> or <c>accesscontrol:write</c> permission.
///     <para>
///         <b>Every create/update/delete/enable-disable action publishes a
///         <see cref="DnsAccessControlRuleChangedEvent" /></b> through
///         <see cref="IDnsServerEventPublisher" /> -- this is what actually invalidates
///         <c>DnsAccessControlRuleCache</c>'s own in-memory snapshot; skipping it on any write path
///         here would mean that write silently has no effect on real query answering until this
///         process restarts.
///     </para>
///     <para>
///         <b>The <see cref="DnsAccessControlRuleWriteRequest.Capabilities" />/
///         <see cref="DnsAccessControlRuleDetail.Capabilities" /> array-of-strings projection</b>
///         is the exact fix <see cref="DnsAccessControlCapabilityEnum" />'s own doc comment calls
///         for: that enum is deliberately not decorated with the
///         <c>[JsonConverter&lt;EnumDescriptionJsonConverter&lt;T&gt;&gt;]</c> every other
///         single-valued enum in this namespace carries (that converter round-trips exactly one
///         <see cref="System.Runtime.Serialization.EnumMemberAttribute" />-mapped string, never a
///         combined <see cref="System.FlagsAttribute" /> value), so this controller converts to
///         and from the stored bitmask itself, at the REST boundary, via
///         <see cref="TryParseCapabilities" />/<see cref="ToCapabilityList" /> -- the same
///         "decouple the wire shape from the stored representation" pattern
///         <see cref="RecordsController.ToCamelCaseDataJson" /> already establishes for
///         <c>RecordEntity.DataJson</c>.
///     </para>
/// </summary>
/// <param name="ruleRepository">The repository this controller reads and writes rules through.</param>
/// <param name="unitOfWork">The unit of work every staged change is committed through.</param>
/// <param name="eventPublisher">
///     The event bus this controller publishes a <see cref="DnsAccessControlRuleChangedEvent" />
///     through on every write, so <c>DnsAccessControlRuleCache</c> reloads its whole-set snapshot
///     (and, independently, the cluster's own <c>AccessControlRuleClusterSyncEventSubscriber</c>
///     forwards the change to every cluster peer) -- see this class's own doc comment.
/// </param>
/// <param name="clusterOptions">
///     This node's own cluster configuration, read only for its own node id -- stamped onto
///     <c>DnsAccessControlRuleEntity.OriginatingNodeId</c> on every create/update, unconditionally,
///     regardless of whether clustering is actually enabled.
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
    ///     This action lists every access-control rule this server knows about, enabled or not.
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

        // Both the page itself and its total count are pushed down to the storage engine -- a
        // real, translated ORDER BY/OFFSET/LIMIT (or the MongoDB equivalent) -- rather than
        // materializing every rule into memory first.
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
    ///     This action looks up a single access-control rule by its identifier.
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
    ///     This action creates a new access-control rule, then publishes a
    ///     <see cref="DnsAccessControlRuleChangedEvent" /> so the real engine's own cache picks it
    ///     up without a restart.
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
    ///     This action fully replaces an existing access-control rule's fields, then publishes a
    ///     <see cref="DnsAccessControlRuleChangedEvent" /> so the real engine's own cache picks up
    ///     the change without a restart.
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
    ///     This action permanently deletes an access-control rule, then publishes a
    ///     <see cref="DnsAccessControlRuleChangedEvent" /> so the real engine's own cache no longer
    ///     grants anything on its behalf.
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
    ///     This method validates that a caller-supplied CIDR value at least parses as an IP
    ///     network -- the same <see cref="System.Net.IPNetwork" /> parser
    ///     <see cref="ClientGroupNetworksController" />'s own <c>TryValidateCidr</c> already uses,
    ///     and <c>DnsAccessControlRuleCache</c> itself matches against at query time -- so a rule
    ///     this controller accepts is guaranteed to actually be usable by the real engine rather
    ///     than silently never matching anything.
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
    ///     This method converts a caller-supplied array of capability names into the stored
    ///     <see cref="DnsAccessControlCapabilityEnum" /> bitmask -- see this controller's own doc
    ///     comment for why this projection exists at all. Each entry must exactly match one
    ///     defined, non-<see cref="DnsAccessControlCapabilityEnum.None" /> member name (matched
    ///     case-sensitively, and rejecting a combined or numeric string via
    ///     <see cref="Enum.IsDefined{TEnum}(TEnum)" />, which only reports <see langword="true" />
    ///     for an exact, single named constant) -- an unrecognized name is a validation error, not
    ///     silently ignored or a <c>500</c>.
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
    ///     This method projects the stored <see cref="DnsAccessControlCapabilityEnum" /> bitmask
    ///     into the explicit array-of-strings shape this controller's REST responses carry -- the
    ///     inverse of <see cref="TryParseCapabilities" />.
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
    ///     This method converts an access-control rule entity into its REST representation.
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
