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
///     This controller administers the entries stored inside a single block/allow list,
///     nested under it (<c>/api/filter-lists/{listId}/entries</c>) since an entry only ever makes
///     sense in its owning list's own context -- the same nesting <see cref="RecordsController" />
///     already establishes for a zone's records. Every action requires the <c>filterlists:read</c>
///     or <c>filterlists:write</c> permission, the same as <see cref="FilterListsController" />.
///     <para>
///         A <see cref="DnsFilterListEntity" /> whose own <see cref="DnsFilterListEntity.Source" />
///         is <see cref="DnsFilterListSourceEnum.Subscribed" /> has its entire entry set replaced
///         wholesale on its own refresh schedule by <c>DnsFilterListExternalRefreshService</c>
///         (via <see cref="IDnsFilterListEntryRepository.ReplaceEntriesAsync" />) -- confirmed by
///         reading that service directly before writing this controller, not assumed. Every
///         write action here (<see cref="Create" />/<see cref="Replace" />/<see cref="Delete" />)
///         is therefore rejected with <c>409 Conflict</c> for a subscribed list: allowing a manual
///         edit that the very next scheduled refresh would silently discard, with no warning to
///         whoever made it, was judged worse than rejecting it outright and pointing the caller at
///         the actual fix (change the list's own <c>source</c> to <c>Local</c> first, via
///         <see cref="FilterListsController.Patch" />, if manual entries are genuinely wanted).
///         <see cref="Get" />/<see cref="List" /> are never blocked -- inspecting a subscribed
///         list's own currently-fetched entries is always useful, only mutating them by hand is
///         the part this project has no honest way to make durable.
///     </para>
///     <para>
///         A subscribed list's own entries are always
///         <see cref="Ninlil.Dns.Common.Enum.DnsFilterEntryKindEnum.Domain" /> -- confirmed
///         by reading <c>DnsFilterListExternalRefreshService.ParseHostsFileFormat</c> directly, a
///         hosts-file-format subscription has no way to express a regex/wildcard entry kind at
///         all, so this controller's own <c>Kind</c> field is only ever meaningful for a
///         <see cref="DnsFilterListSourceEnum.Local" /> list's own manually-entered rows anyway.
///         <see cref="Create" />/
///         <see cref="Replace" /> both reject a malformed regex/wildcard pattern with
///         <c>400 Bad Request</c> by actually attempting to compile it (via
///         <c>DnsFilterEngine.CompileOrNull</c>/<c>DnsFilterEngine.BuildWildcardRegexPattern</c>,
///         the exact same compilation this controller's own pattern must later pass at query
///         time) before it is ever persisted -- never merely discovered later, at query time, by
///         <c>DnsFilterEngine</c> itself.
///     </para>
/// </summary>
/// <param name="filterListRepository">The repository used to confirm the owning list exists and to check its own <see cref="DnsFilterListEntity.Source" />.</param>
/// <param name="entryRepository">The repository this controller reads and writes entries through directly.</param>
/// <param name="unitOfWork">The unit of work every staged change is committed through.</param>
/// <param name="eventPublisher">
///     The event bus this controller publishes a <see cref="DnsFilterListEntryChangedEvent" />
///     through on every write, so the cluster's own <c>FilterListEntryClusterSyncEventSubscriber</c>
///     forwards the change to every cluster peer.
/// </param>
/// <param name="clusterOptions">
///     This node's own cluster configuration, read only for its own node id -- stamped onto
///     <c>DnsFilterListEntryEntity.OriginatingNodeId</c> on every create/update.
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
    ///     This action lists every entry stored in a list.
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

        // Both the page itself and its total count are pushed down to the storage engine -- a
        // real, translated WHERE/ORDER BY/OFFSET/LIMIT (or the MongoDB equivalent) -- rather than
        // materializing every entry into memory first. This matters more here than for this
        // project's other, lower-cardinality entities: a subscribed external block list can carry
        // tens of thousands of entries.
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
    ///     This action looks up a single entry by its identifier.
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
    ///     This action adds a new entry to a <see cref="DnsFilterListSourceEnum.Local" /> list.
    ///     Rejected with <c>409 Conflict</c> for a <see cref="DnsFilterListSourceEnum.Subscribed" />
    ///     list -- see this controller's own summary for why.
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
    ///     This action fully replaces an existing entry's own value/comment, keeping its own
    ///     identifier. Rejected with <c>409 Conflict</c> for a subscribed list, the same as
    ///     <see cref="Create" />.
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
    ///     This action removes an entry from the list. Rejected with <c>409 Conflict</c> for a
    ///     subscribed list, the same as <see cref="Create" />/<see cref="Replace" />.
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
    ///     This method throws a <see cref="ConflictException" /> when a write is attempted against
    ///     a <see cref="DnsFilterListSourceEnum.Subscribed" /> list, or does nothing at all
    ///     otherwise -- see this controller's own summary for the full reasoning.
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
    ///     This method throws a <see cref="ValidationException" /> naming exactly which part of a
    ///     write request's own <c>value</c>/<c>kind</c> pair is invalid -- a
    ///     malformed regex/wildcard pattern must be rejected here, at write time, never merely
    ///     discovered later at query time by <c>DnsFilterEngine</c> (which itself only ever treats
    ///     an unparseable pattern as a silent non-match, by design, since the query-answering path
    ///     can never throw). A <see cref="DnsFilterEntryKindEnum.Wildcard" />/
    ///     <see cref="DnsFilterEntryKindEnum.Regex" /> kind is additionally rejected outright
    ///     against a non-<see cref="DnsFilterListKindEnum.Domain" /> list -- neither syntax has any
    ///     meaning for an IP address/CIDR network entry. Does nothing at all when the request is
    ///     entirely valid.
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
    ///     This method converts a filter list entry entity into its REST representation.
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
            entry.OriginatingNodeId
        );
}
