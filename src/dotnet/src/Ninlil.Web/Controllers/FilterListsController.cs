using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Filtering;
using Ninlil.Web.Examples.FilterLists;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller administers this server's own block/allow lists -- the top-level
///     resource; a list's own entries are managed separately, through the nested
///     <see cref="FilterListEntriesController" /> (<c>/api/filter-lists/{listId}/entries</c>).
///     Every action requires the <c>filterlists:read</c> or <c>filterlists:write</c> permission --
///     reused for the nested entries controller too, the same way <c>zones:read</c>/<c>zones:write</c>
///     already cover <see cref="RecordsController" /> as a zone's own child resource.
/// </summary>
/// <param name="filterListRepository">The repository this controller reads and writes lists through.</param>
/// <param name="listAssignmentRepository">
///     The repository used to remove a deleted list's own client-group assignments -- see
///     <see cref="Delete" /> for why.
/// </param>
/// <param name="unitOfWork">The unit of work every staged change is committed through.</param>
/// <param name="eventPublisher">
///     The event bus this controller publishes a <see cref="DnsFilterListChangedEvent" /> through
///     on every write, so the cluster's own <c>FilterListClusterSyncEventSubscriber</c> forwards
///     the change to every cluster peer.
/// </param>
/// <param name="clusterOptions">
///     This node's own cluster configuration, read only for its own node id -- stamped onto
///     <c>DnsFilterListEntity.OriginatingNodeId</c> on every create/update.
/// </param>
[ApiController]
[Route("api/filter-lists")]
public class FilterListsController(
    IDnsFilterListRepository filterListRepository,
    IDnsClientGroupListAssignmentRepository listAssignmentRepository,
    IUnitOfWork unitOfWork,
    IDnsServerEventPublisher eventPublisher,
    IOptionsMonitor<ClusterOptions> clusterOptions
) : ControllerBase
{
    /// <summary>
    ///     This action lists every block/allow list this server knows about.
    /// </summary>
    [HttpGet]
    [RequirePermission("filterlists:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(FilterListDetailPagedResultExample))]
    public async Task<ActionResult<PagedResult<FilterListDetail>>> List(
        [FromQuery] PaginationQuery pagination,
        CancellationToken stoppingToken
    )
    {
        (int skip, int take) = (pagination ?? new PaginationQuery()).Normalize();

        // Both the page itself and its total count are pushed down to the storage engine -- a
        // real, translated ORDER BY/OFFSET/LIMIT (or the MongoDB equivalent) -- rather than
        // materializing every list into memory first.
        IReadOnlyList<DnsFilterListEntity> lists = await filterListRepository.SearchAsync(
            skip,
            take,
            stoppingToken
        );
        int totalCount = await filterListRepository.CountAsync(stoppingToken);

        return Ok(
            new PagedResult<FilterListDetail>([.. lists.Select(ToDetail)], skip, take, totalCount)
        );
    }

    /// <summary>
    ///     This action looks up a single block/allow list by its identifier.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("filterlists:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(FilterListDetailExample))]
    public async Task<ActionResult<FilterListDetail>> Get(Guid id, CancellationToken stoppingToken)
    {
        DnsFilterListEntity list = await filterListRepository.GetByIdAsync(id, stoppingToken);
        if (list is null)
            throw new NotFoundException("FilterList", id.ToString());

        return Ok(ToDetail(list));
    }

    /// <summary>
    ///     This action creates a new block/allow list.
    /// </summary>
    [HttpPost]
    [RequirePermission("filterlists:write")]
    [SwaggerRequestExample(typeof(FilterListWriteRequest), typeof(FilterListWriteRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status201Created, typeof(FilterListDetailExample))]
    public async Task<ActionResult<FilterListDetail>> Create(
        FilterListWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        if (string.IsNullOrWhiteSpace(request?.Name))
            throw new ValidationException("'name' is required.");

        if (await filterListRepository.FindByNameAsync(request.Name, stoppingToken) is not null)
            throw new ConflictException($"A filter list named '{request.Name}' already exists.");

        if (
            !TryValidateSubscription(
                request.Source,
                request.SubscriptionUrl,
                out string subscriptionError
            )
        )
            throw new ValidationException(subscriptionError);

        DnsFilterListEntity list = new()
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Kind = request.Kind,
            Action = request.Action,
            Source = request.Source,
            SubscriptionUrl =
                request.Source == DnsFilterListSourceEnum.Subscribed
                    ? request.SubscriptionUrl
                    : null,
            RefreshInterval =
                request.Source == DnsFilterListSourceEnum.Subscribed
                    ? request.RefreshInterval
                    : null,
            IsDisabled = request.IsDisabled,
            Comments = request.Comments,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            OriginatingNodeId = clusterOptions.CurrentValue.NodeId,
        };

        await filterListRepository.AddAsync(list, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        eventPublisher.Publish(new DnsFilterListChangedEvent(DateTimeOffset.UtcNow, list.Id));

        return CreatedAtAction(nameof(Get), new { id = list.Id }, ToDetail(list));
    }

    /// <summary>
    ///     This action fully replaces an existing block/allow list's metadata. Note that changing
    ///     <see cref="FilterListWriteRequest.Source" /> from <see cref="DnsFilterListSourceEnum.Local" />
    ///     to <see cref="DnsFilterListSourceEnum.Subscribed" /> does not itself touch the list's
    ///     existing entries -- they remain as-is until the next scheduled refresh replaces them
    ///     wholesale.
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("filterlists:write")]
    [SwaggerRequestExample(typeof(FilterListWriteRequest), typeof(FilterListWriteRequestExample))]
    public async Task<IActionResult> Replace(
        Guid id,
        FilterListWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        DnsFilterListEntity list = await filterListRepository.GetByIdAsync(id, stoppingToken);
        if (list is null)
            throw new NotFoundException("FilterList", id.ToString());

        if (string.IsNullOrWhiteSpace(request?.Name))
            throw new ValidationException("'name' is required.");

        DnsFilterListEntity existingByName = await filterListRepository.FindByNameAsync(
            request.Name,
            stoppingToken
        );
        if (existingByName is not null && existingByName.Id != id)
            throw new ConflictException($"A filter list named '{request.Name}' already exists.");

        if (
            !TryValidateSubscription(
                request.Source,
                request.SubscriptionUrl,
                out string subscriptionError
            )
        )
            throw new ValidationException(subscriptionError);

        list.Name = request.Name;
        list.Kind = request.Kind;
        list.Action = request.Action;
        list.Source = request.Source;
        list.SubscriptionUrl =
            request.Source == DnsFilterListSourceEnum.Subscribed ? request.SubscriptionUrl : null;
        list.RefreshInterval =
            request.Source == DnsFilterListSourceEnum.Subscribed ? request.RefreshInterval : null;
        list.IsDisabled = request.IsDisabled;
        list.Comments = request.Comments;
        list.UpdatedAtUtc = DateTimeOffset.UtcNow;
        list.OriginatingNodeId = clusterOptions.CurrentValue.NodeId;

        filterListRepository.Update(list);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        eventPublisher.Publish(new DnsFilterListChangedEvent(DateTimeOffset.UtcNow, list.Id));

        return NoContent();
    }

    /// <summary>
    ///     This action partially updates an existing block/allow list's metadata via RFC 7396 JSON
    ///     Merge Patch: a field absent from the patch document is left untouched, a field present
    ///     with a JSON <c>null</c> clears it (where the underlying field is itself nullable), and
    ///     any other present field overwrites it.
    /// </summary>
    [HttpPatch("{id:guid}")]
    [RequirePermission("filterlists:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(FilterListPatchRequestExample))]
    public async Task<IActionResult> Patch(
        Guid id,
        [FromBody] JsonElement patch,
        CancellationToken stoppingToken
    )
    {
        DnsFilterListEntity list = await filterListRepository.GetByIdAsync(id, stoppingToken);
        if (list is null)
            throw new NotFoundException("FilterList", id.ToString());

        if (patch.TryGetMergePatchField("name", out JsonElement name))
        {
            string newName = name.GetString();
            if (string.IsNullOrWhiteSpace(newName))
                throw new ValidationException("'name' must not be empty.");

            DnsFilterListEntity existingByName = await filterListRepository.FindByNameAsync(
                newName,
                stoppingToken
            );
            if (existingByName is not null && existingByName.Id != id)
                throw new ConflictException($"A filter list named '{newName}' already exists.");

            list.Name = newName;
        }

        if (patch.TryGetMergePatchField("kind", out JsonElement kind))
            list.Kind = JsonSerializer.Deserialize<DnsFilterListKindEnum>(kind.GetRawText());

        if (patch.TryGetMergePatchField("action", out JsonElement action))
            list.Action = JsonSerializer.Deserialize<DnsFilterListActionEnum>(action.GetRawText());

        DnsFilterListSourceEnum source = list.Source;
        if (patch.TryGetMergePatchField("source", out JsonElement sourceElement))
            source = JsonSerializer.Deserialize<DnsFilterListSourceEnum>(
                sourceElement.GetRawText()
            );

        string subscriptionUrl = list.SubscriptionUrl;
        bool subscriptionUrlPresent = patch.TryGetMergePatchField(
            "subscriptionUrl",
            out JsonElement subscriptionUrlElement
        );
        if (subscriptionUrlPresent)
            subscriptionUrl =
                subscriptionUrlElement.ValueKind == JsonValueKind.Null
                    ? null
                    : subscriptionUrlElement.GetString();

        if (!TryValidateSubscription(source, subscriptionUrl, out string subscriptionError))
            throw new ValidationException(subscriptionError);

        list.Source = source;
        list.SubscriptionUrl =
            source == DnsFilterListSourceEnum.Subscribed ? subscriptionUrl : null;

        if (patch.TryGetMergePatchField("refreshInterval", out JsonElement refreshInterval))
            list.RefreshInterval =
                refreshInterval.ValueKind == JsonValueKind.Null
                    ? null
                    : JsonSerializer.Deserialize<TimeSpan>(refreshInterval.GetRawText());

        if (source != DnsFilterListSourceEnum.Subscribed)
            list.RefreshInterval = null;

        if (patch.TryGetMergePatchField("isDisabled", out JsonElement isDisabled))
            list.IsDisabled = isDisabled.GetBoolean();

        if (patch.TryGetMergePatchField("comments", out JsonElement comments))
            list.Comments = comments.ValueKind == JsonValueKind.Null ? null : comments.GetString();

        list.UpdatedAtUtc = DateTimeOffset.UtcNow;
        list.OriginatingNodeId = clusterOptions.CurrentValue.NodeId;
        filterListRepository.Update(list);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        eventPublisher.Publish(new DnsFilterListChangedEvent(DateTimeOffset.UtcNow, list.Id));

        return NoContent();
    }

    /// <summary>
    ///     This action permanently deletes a block/allow list. Any client-group assignment still
    ///     referencing it (see <see cref="ClientGroupListAssignmentsController" />) is removed
    ///     first -- unlike <see cref="TsigKeysController.Delete" />'s deliberately lax stance on a
    ///     zone's own soft-reference <c>TsigKeyName</c>, <see cref="DnsFilterEngine" /> already
    ///     tolerates a dangling assignment gracefully (confirmed by inspection: it simply skips a
    ///     list it cannot find), but this project cleans it up explicitly anyway to avoid
    ///     accumulating orphaned join rows, the same reasoning <see cref="ZonesController.Delete" />
    ///     already applies to a deleted zone's own records and DNSSEC keys.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("filterlists:write")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken stoppingToken)
    {
        DnsFilterListEntity list = await filterListRepository.GetByIdAsync(id, stoppingToken);
        if (list is null)
            throw new NotFoundException("FilterList", id.ToString());

        IReadOnlyList<DnsClientGroupListAssignmentEntity> assignments =
            await listAssignmentRepository.ListAsync(
                assignment => assignment.ListId == id,
                stoppingToken
            );
        foreach (DnsClientGroupListAssignmentEntity assignment in assignments)
            listAssignmentRepository.Remove(assignment);

        filterListRepository.Remove(list);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        eventPublisher.Publish(
            new DnsFilterListChangedEvent(DateTimeOffset.UtcNow, id, IsDeleted: true)
        );

        return NoContent();
    }

    /// <summary>
    ///     This method validates that a <see cref="DnsFilterListSourceEnum.Subscribed" /> list
    ///     supplies a subscription URL, mirroring the same expectation
    ///     <c>DnsFilterListExternalRefreshService</c> itself has (it silently does nothing for a
    ///     subscribed list with no URL, rather than erroring -- this REST boundary is stricter,
    ///     giving the caller an immediate <c>400</c> instead).
    /// </summary>
    private static bool TryValidateSubscription(
        DnsFilterListSourceEnum source,
        string subscriptionUrl,
        out string error
    )
    {
        error = null;

        if (
            source == DnsFilterListSourceEnum.Subscribed
            && string.IsNullOrWhiteSpace(subscriptionUrl)
        )
        {
            error = "'subscriptionUrl' is required when 'source' is 'Subscribed'.";
            return false;
        }

        return true;
    }

    /// <summary>
    ///     This method converts a filter list entity into its REST representation.
    /// </summary>
    private static FilterListDetail ToDetail(DnsFilterListEntity list) =>
        new(
            list.Id,
            list.Name,
            list.Kind,
            list.Action,
            list.Source,
            list.SubscriptionUrl,
            list.RefreshInterval,
            list.LastFetchedAtUtc,
            list.IsDisabled,
            list.Comments,
            list.CreatedAtUtc,
            list.UpdatedAtUtc,
            list.OriginatingNodeId
        );
}
