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
using Ninlil.Web.Examples.FilterListEntries;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller manages the entries of one block or allow list, nested under it at
///     <c>/api/filter-lists/{listId}/entries</c>, as <see cref="RecordsController" /> is nested
///     under a zone. Reads need <c>filterlists:read</c> and changes need
///     <c>filterlists:write</c>, as for <see cref="FilterListsController" />.
///     <para>
///         <b>Entries of a subscribed list cannot be edited.</b>
///         <c>DnsFilterListExternalRefreshService</c> replaces a
///         <see cref="DnsFilterListSourceEnum.Subscribed" /> list's whole entry set on each
///         refresh (through <see cref="IDnsFilterListEntryRepository.ReplaceEntriesAsync" />), so a
///         manual edit would be silently lost. <see cref="Create" />, <see cref="Replace" /> and
///         <see cref="Delete" /> therefore return 409 for such a list; change its <c>source</c> to
///         <c>Local</c> through <see cref="FilterListsController.Patch" /> to edit by hand.
///         <see cref="Get" /> and <see cref="List" /> always work.
///     </para>
///     <para>
///         A subscribed list's entries are always
///         <see cref="Ninlil.Dns.Common.Enum.DnsFilterEntryKindEnum.Domain" />:
///         <see cref="DnsFilterListParser" /> skips adblock wildcard and regex rules rather than
///         translating them, so the entry <c>Kind</c> matters only for a local list.
///         <see cref="Create" /> and <see cref="Replace" /> compile a regex or wildcard pattern
///         with <c>DnsFilterEngine.CompileOrNull</c> (and
///         <c>DnsFilterEngine.BuildWildcardRegexPattern</c>), the same compilation the engine uses
///         at query time, and return 400 for one that fails.
///     </para>
/// </summary>
/// <param name="filterListRepository">Confirms the list exists and supplies its <see cref="DnsFilterListEntity.Source" />.</param>
/// <param name="entryRepository">Reads and writes entries.</param>
/// <param name="unitOfWork">Commits staged changes.</param>
/// <param name="eventPublisher">
///     Publishes <see cref="DnsFilterListEntryChangedEvent" /> on every write so
///     <c>FilterListEntryClusterSyncEventSubscriber</c> sends the change to every cluster peer.
/// </param>
/// <param name="clusterOptions">
///     Supplies this node's cluster node id, stamped onto
///     <c>DnsFilterListEntryEntity.OriginatingNodeId</c> on every create and update.
/// </param>
[ApiController]
[Route("api/filter-lists/{listId:guid}/entries")]
public class FilterListEntriesController(
    IDnsFilterListRepository filterListRepository,
    IDnsFilterListEntryRepository entryRepository,
    IUnitOfWork unitOfWork,
    IDnsServerEventPublisher eventPublisher,
    IOptionsMonitor<ClusterOptions> clusterOptions
) : ControllerBase
{
    /// <summary>
    ///     This action lists a list's entries, one page at a time. Returns 404 for an unknown list.
    ///     Requires <c>filterlists:read</c>.
    /// </summary>
    [HttpGet]
    [RequirePermission("filterlists:read")]
    [SwaggerResponseExample(
        StatusCodes.Status200OK,
        typeof(FilterListEntryDetailPagedResultExample)
    )]
    public async Task<ActionResult<PagedResult<FilterListEntryDetail>>> List(
        Guid listId,
        [FromQuery] PaginationQuery pagination,
        CancellationToken stoppingToken
    )
    {
        if (await filterListRepository.GetByIdAsync(listId, stoppingToken) is null)
            throw new NotFoundException("FilterList", listId.ToString());

        (int skip, int take) = (pagination ?? new PaginationQuery()).Normalize();

        // The page and the total count are both computed by the storage engine
        // (WHERE/ORDER BY/OFFSET/LIMIT or the MongoDB equivalent), not by loading every entry into
        // memory. It matters here: a subscribed list can hold tens of thousands of entries.
        IReadOnlyList<DnsFilterListEntryEntity> entries = await entryRepository.SearchByListIdAsync(
            listId,
            skip,
            take,
            stoppingToken
        );
        int totalCount = await entryRepository.CountByListIdAsync(listId, stoppingToken);

        return Ok(
            new PagedResult<FilterListEntryDetail>(
                [.. entries.Select(ToDetail)],
                skip,
                take,
                totalCount
            )
        );
    }

    /// <summary>
    ///     This action returns one entry. Returns 404 when it does not exist or belongs to another
    ///     list. Requires <c>filterlists:read</c>.
    /// </summary>
    [HttpGet("{id:guid}")]
    [RequirePermission("filterlists:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(FilterListEntryDetailExample))]
    public async Task<ActionResult<FilterListEntryDetail>> Get(
        Guid listId,
        Guid id,
        CancellationToken stoppingToken
    )
    {
        DnsFilterListEntryEntity entry = await entryRepository.GetByIdAsync(id, stoppingToken);
        if (entry is null || entry.ListId != listId)
            throw new NotFoundException("FilterListEntry", id.ToString());

        return Ok(ToDetail(entry));
    }

    /// <summary>
    ///     This action adds an entry to a <see cref="DnsFilterListSourceEnum.Local" /> list.
    ///     Returns 400 for a missing value, a wildcard or regex entry in a non-domain list, or a
    ///     pattern that does not compile; 404 for an unknown list; and 409 for a subscribed list.
    ///     Requires <c>filterlists:write</c>.
    /// </summary>
    [HttpPost]
    [RequirePermission("filterlists:write")]
    [SwaggerRequestExample(
        typeof(FilterListEntryWriteRequest),
        typeof(FilterListEntryWriteRequestExample)
    )]
    [SwaggerResponseExample(StatusCodes.Status201Created, typeof(FilterListEntryDetailExample))]
    public async Task<ActionResult<FilterListEntryDetail>> Create(
        Guid listId,
        FilterListEntryWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        DnsFilterListEntity list = await filterListRepository.GetByIdAsync(listId, stoppingToken);
        if (list is null)
            throw new NotFoundException("FilterList", listId.ToString());

        ValidateNotSubscribedOrThrow(list);
        ValidateEntryRequestOrThrow(list, request);

        DnsFilterListEntryEntity entry = new()
        {
            Id = Guid.NewGuid(),
            ListId = listId,
            Value = request.Value,
            Comment = request.Comment,
            Kind = request.Kind,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            OriginatingNodeId = clusterOptions.CurrentValue.NodeId,
        };

        await entryRepository.AddAsync(entry, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        eventPublisher.Publish(
            new DnsFilterListEntryChangedEvent(DateTimeOffset.UtcNow, listId, entry.Id)
        );

        return CreatedAtAction(nameof(Get), new { listId, id = entry.Id }, ToDetail(entry));
    }

    /// <summary>
    ///     This action replaces an entry's value, comment and kind, keeping its id. Validation and
    ///     errors are as for <see cref="Create" />, plus 404 for an unknown entry. Requires
    ///     <c>filterlists:write</c>.
    /// </summary>
    [HttpPut("{id:guid}")]
    [RequirePermission("filterlists:write")]
    [SwaggerRequestExample(
        typeof(FilterListEntryWriteRequest),
        typeof(FilterListEntryWriteRequestExample)
    )]
    public async Task<IActionResult> Replace(
        Guid listId,
        Guid id,
        FilterListEntryWriteRequest request,
        CancellationToken stoppingToken
    )
    {
        DnsFilterListEntity list = await filterListRepository.GetByIdAsync(listId, stoppingToken);
        if (list is null)
            throw new NotFoundException("FilterList", listId.ToString());

        DnsFilterListEntryEntity entry = await entryRepository.GetByIdAsync(id, stoppingToken);
        if (entry is null || entry.ListId != listId)
            throw new NotFoundException("FilterListEntry", id.ToString());

        ValidateNotSubscribedOrThrow(list);
        ValidateEntryRequestOrThrow(list, request);

        entry.Value = request.Value;
        entry.Comment = request.Comment;
        entry.Kind = request.Kind;
        entry.UpdatedAtUtc = DateTimeOffset.UtcNow;
        entry.OriginatingNodeId = clusterOptions.CurrentValue.NodeId;

        entryRepository.Update(entry);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        eventPublisher.Publish(
            new DnsFilterListEntryChangedEvent(DateTimeOffset.UtcNow, listId, entry.Id)
        );

        return NoContent();
    }

    /// <summary>
    ///     This action removes an entry from the list. Returns 404 for an unknown list or entry and
    ///     409 for a subscribed list. Requires <c>filterlists:write</c>.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission("filterlists:write")]
    public async Task<IActionResult> Delete(Guid listId, Guid id, CancellationToken stoppingToken)
    {
        DnsFilterListEntity list = await filterListRepository.GetByIdAsync(listId, stoppingToken);
        if (list is null)
            throw new NotFoundException("FilterList", listId.ToString());

        DnsFilterListEntryEntity entry = await entryRepository.GetByIdAsync(id, stoppingToken);
        if (entry is null || entry.ListId != listId)
            throw new NotFoundException("FilterListEntry", id.ToString());

        ValidateNotSubscribedOrThrow(list);

        entryRepository.Remove(entry);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        eventPublisher.Publish(
            new DnsFilterListEntryChangedEvent(DateTimeOffset.UtcNow, listId, id, IsDeleted: true)
        );

        return NoContent();
    }

    /// <summary>
    ///     This method throws <see cref="ConflictException" /> when the list is
    ///     <see cref="DnsFilterListSourceEnum.Subscribed" /> (see the class summary).
    /// </summary>
    private static void ValidateNotSubscribedOrThrow(DnsFilterListEntity list)
    {
        if (list.Source != DnsFilterListSourceEnum.Subscribed)
            return;

        throw new ConflictException(
            $"'{list.Name}' is a subscribed list; its entries are periodically replaced wholesale from '{list.SubscriptionUrl}' and cannot be edited manually. Change the list's own 'source' to 'Local' first if manual entries are wanted."
        );
    }

    /// <summary>
    ///     This method throws <see cref="ValidationException" /> naming what is wrong with a
    ///     request's <c>value</c> and <c>kind</c>. A bad regex or wildcard pattern must be caught
    ///     here, because <c>DnsFilterEngine</c> treats an unparseable pattern as a silent non-match
    ///     (the query path never throws). A wildcard or regex entry is also rejected for a list
    ///     that is not a <see cref="DnsFilterListKindEnum.Domain" /> list, since neither means
    ///     anything for an IP address or network.
    /// </summary>
    private static void ValidateEntryRequestOrThrow(
        DnsFilterListEntity list,
        FilterListEntryWriteRequest request
    )
    {
        if (string.IsNullOrWhiteSpace(request?.Value))
            throw new ValidationException("'value' is required.");

        if (
            request.Kind != DnsFilterEntryKindEnum.Domain
            && list.Kind != DnsFilterListKindEnum.Domain
        )
            throw new ValidationException(
                $"A '{request.Kind}' entry is only meaningful in a '{DnsFilterListKindEnum.Domain}'-kind list; '{list.Name}' is a '{list.Kind}'-kind list."
            );

        if (
            request.Kind == DnsFilterEntryKindEnum.Regex
            && DnsFilterEngine.CompileOrNull(request.Value) is null
        )
            throw new ValidationException($"'{request.Value}' is not a valid regular expression.");

        if (
            request.Kind == DnsFilterEntryKindEnum.Wildcard
            && DnsFilterEngine.CompileOrNull(
                DnsFilterEngine.BuildWildcardRegexPattern(request.Value)
            )
                is null
        )
            throw new ValidationException($"'{request.Value}' is not a valid wildcard pattern.");
    }

    /// <summary>
    ///     This method converts an entry entity to its API representation.
    /// </summary>
    private static FilterListEntryDetail ToDetail(DnsFilterListEntryEntity entry) =>
        new(
            entry.Id,
            entry.ListId,
            entry.Value,
            entry.Comment,
            entry.Kind,
            entry.CreatedAtUtc,
            entry.UpdatedAtUtc,
            entry.OriginatingNodeId,
            entry.IsException
        );
}
