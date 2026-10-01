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
///     This controller manages block and allow lists. A list's entries are managed through the
///     nested <see cref="FilterListEntriesController" />
///     (<c>/api/filter-lists/{listId}/entries</c>), which uses the same permissions:
///     <c>filterlists:read</c> for reads and <c>filterlists:write</c> for changes, as
///     <c>zones:read</c> and <c>zones:write</c> cover <see cref="RecordsController" />.
/// </summary>
/// <param name="filterListRepository">Reads and writes lists.</param>
/// <param name="listAssignmentRepository">
///     Removes a deleted list's client-group assignments (see <see cref="Delete" />).
/// </param>
/// <param name="unitOfWork">Commits staged changes.</param>
/// <param name="eventPublisher">
///     Publishes <see cref="DnsFilterListChangedEvent" /> on every write so
///     <c>FilterListClusterSyncEventSubscriber</c> sends the change to every cluster peer.
/// </param>
/// <param name="clusterOptions">
///     Supplies this node's cluster node id, stamped onto
///     <c>DnsFilterListEntity.OriginatingNodeId</c> on every create and update.
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
    ///     This action lists block and allow lists, one page at a time. Requires
    ///     <c>filterlists:read</c>.
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

        // The page and the total count are both computed by the storage engine (ORDER
        // BY/OFFSET/LIMIT or the MongoDB equivalent), not by loading every list into memory.
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
    ///     This action returns one list. Returns 404 for an unknown id. Requires
    ///     <c>filterlists:read</c>.
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
    ///     This action creates a block or allow list. A subscribed list needs a subscription URL;
    ///     the URL and refresh interval are dropped for a local list. Returns 400 for a missing
    ///     name or URL and 409 when the name is taken. Requires <c>filterlists:write</c>.
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
    ///     This action replaces a list's settings. Changing
    ///     <see cref="FilterListWriteRequest.Source" /> from
    ///     <see cref="DnsFilterListSourceEnum.Local" /> to
    ///     <see cref="DnsFilterListSourceEnum.Subscribed" /> leaves the existing entries alone
    ///     until the next refresh replaces them all. Validation and errors are as for
    ///     <see cref="Create" />, plus 404 for an unknown id. Requires <c>filterlists:write</c>.
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
    ///     This action updates a list's settings as an RFC 7396 merge patch: an absent field is
    ///     unchanged, a JSON <c>null</c> clears a nullable field, and any other value replaces it.
    ///     Validation and errors are as for <see cref="Replace" />. Requires
    ///     <c>filterlists:write</c>.
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
    ///     This action deletes a list and the client-group assignments that refer to it (see
    ///     <see cref="ClientGroupListAssignmentsController" />). <see cref="DnsFilterEngine" />
    ///     would skip a missing list anyway, but the assignments are removed so join rows do not
    ///     pile up. The list's entries and any direct client assignments are not removed. Returns
    ///     404 for an unknown id. Requires <c>filterlists:write</c>.
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
    ///     This method requires a subscription URL for a
    ///     <see cref="DnsFilterListSourceEnum.Subscribed" /> list.
    ///     <c>DnsFilterListExternalRefreshService</c> silently skips a subscribed list without one,
    ///     so this check returns a 400 instead.
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
    ///     This method converts a list entity to its API representation.
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
