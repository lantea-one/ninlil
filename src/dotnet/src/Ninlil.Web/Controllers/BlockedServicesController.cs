using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Filtering.BlockedServices;
using Ninlil.Web.Examples.BlockedServices;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller administers Ninlil's own curated "blocked services" catalog
///     (<c>BlockedServiceCatalog</c>) -- the one-click named-service toggles a homelab operator
///     uses instead of hand-authoring a filter list for a specific consumer service. It is a thin
///     layer over the existing block/allow-list engine already covered by
///     <see cref="FilterListsController" />/<see cref="FilterListEntriesController" />, not a
///     parallel blocking mechanism: enabling a catalog entry creates (or re-enables) exactly one
///     ordinary, <c>Subscribed</c>-source <see cref="DnsFilterListEntity" /> pointed at that
///     entry's own real third-party domain list, and disabling it removes that same list. See
///     <c>BlockedServiceCatalog</c>'s own doc comment for this catalog's data source, license, and
///     exact scope. Every action requires the <c>filterlists:read</c> or <c>filterlists:write</c>
///     permission -- the same permissions <see cref="FilterListsController" /> itself requires,
///     since this controller only ever creates/removes an ordinary <see cref="DnsFilterListEntity" />
///     row, ordinary Filter Lists permissions already cover exactly the access this needs.
///     <para>
///         <b>How a catalog entry's own managed list is found again</b>: by matching a
///         <see cref="DnsFilterListSourceEnum.Subscribed" /> list's own
///         <see cref="DnsFilterListEntity.SubscriptionUrl" /> against the catalog entry's own
///         <see cref="BlockedServiceCatalogEntry.SubscriptionUrl" /> exactly -- stable even if an
///         operator renames the list afterward (matching by <c>Name</c> would not be).
///     </para>
///     <para>
///         <b>Per-client/client-group targeting (optional <c>clientId</c>/<c>clientGroupId</c>
///         query parameters, mutually exclusive, on every action below)</b>: enabling/disabling a
///         catalog entry always creates (or re-enables)/removes exactly one shared, global
///         <see cref="DnsFilterListEntity" /> -- that part is unchanged and, with neither
///         parameter given, every action here behaves byte-for-byte as it always has. What the
///         parameters add is the second, separate step this feature always needed to actually
///         apply to anyone: <c>Enable</c> additionally creates a
///         <see cref="Ninlil.Dns.Data.Abstractions.Entity.DnsClientListAssignmentEntity" />/
///         <see cref="DnsClientGroupListAssignmentEntity" /> row (whichever the given parameter
///         names) linking that shared list straight to the named client/group, via the exact same
///         repositories <see cref="ClientListAssignmentsController" />/
///         <see cref="ClientGroupListAssignmentsController" /> already expose -- no new assignment
///         mechanism, this is just doing, in one call, the second REST call an operator would
///         otherwise have to make by hand. <c>Disable</c> with a target removes only that one
///         target's own assignment, leaving the shared list itself, and every other target's own
///         assignment, completely untouched -- <c>Disable</c> with no target keeps doing exactly
///         what it always has (permanently deletes the shared list and every group assignment
///         referencing it), extended only to also clean up any client-level assignment referencing
///         it now that one can exist at all (a real, necessary addition, not a behavior change to
///         any flow that predates this capability, since no such assignment could have existed
///         before it).
///     </para>
/// </summary>
/// <param name="filterListRepository">The repository this controller reads and writes managed filter lists through.</param>
/// <param name="listAssignmentRepository">
///     The repository used to remove a disabled entry's own client-group assignments, mirroring
///     <see cref="FilterListsController.Delete" />'s own identical cleanup, and to create/remove a
///     <c>clientGroupId</c>-targeted assignment.
/// </param>
/// <param name="unitOfWork">The unit of work every staged change is committed through.</param>
/// <param name="clientRepository">The repository used to confirm an optional <c>clientId</c> target actually exists.</param>
/// <param name="clientGroupRepository">The repository used to confirm an optional <c>clientGroupId</c> target actually exists.</param>
/// <param name="clientListAssignmentRepository">The repository used to create/remove a <c>clientId</c>-targeted assignment.</param>
[ApiController]
[Route("api/blocked-services")]
public class BlockedServicesController(
    IDnsFilterListRepository filterListRepository,
    IDnsClientGroupListAssignmentRepository listAssignmentRepository,
    IUnitOfWork unitOfWork,
    IDnsClientRepository clientRepository,
    IDnsClientGroupRepository clientGroupRepository,
    IDnsClientListAssignmentRepository clientListAssignmentRepository
) : ControllerBase
{
    /// <summary>
    ///     This constant contains how often a catalog entry's own managed list is refetched once
    ///     enabled, matching <c>DnsServerPresetCatalog</c>'s own identical default for a
    ///     preset-owned subscribed list.
    /// </summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(24);

    /// <summary>
    ///     This action lists every catalog entry this server offers, together with whether each is
    ///     currently enabled -- globally by default, or for a single optional <c>clientId</c>/
    ///     <c>clientGroupId</c> target (mutually exclusive) when named, see this class's own doc
    ///     comment for the full targeting contract.
    /// </summary>
    [HttpGet]
    [RequirePermission("filterlists:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(BlockedServiceListExample))]
    public async Task<ActionResult<IReadOnlyList<BlockedServiceDetail>>> List(
        [FromQuery] Guid? clientId,
        [FromQuery] Guid? clientGroupId,
        CancellationToken stoppingToken
    )
    {
        ValidateMutuallyExclusive(clientId, clientGroupId);

        IReadOnlyList<DnsFilterListEntity> managedLists = await ListManagedAsync(stoppingToken);
        List<BlockedServiceDetail> details = [];

        foreach (BlockedServiceCatalogEntry entry in BlockedServiceCatalog.Entries)
            details.Add(
                await ToDetailAsync(
                    entry,
                    FindManagedList(entry, managedLists),
                    clientId,
                    clientGroupId,
                    stoppingToken
                )
            );

        return Ok(details);
    }

    /// <summary>
    ///     This action enables a single catalog entry: creates its own managed
    ///     <see cref="DnsFilterListEntity" /> if none exists yet, or re-enables (clears
    ///     <see cref="DnsFilterListEntity.IsDisabled" /> on) one that was previously disabled --
    ///     unaffected by scoping, see this class's own doc comment. Idempotent -- enabling an
    ///     already-enabled entry (in the same scope) simply returns its current state. When a
    ///     single optional <c>clientId</c>/<c>clientGroupId</c> target (mutually exclusive) is
    ///     given, this action additionally creates that target's own assignment of the managed
    ///     list, if it does not already have one -- rejected with <c>400 Bad Request</c> if both
    ///     are given, or if the named target does not actually exist.
    /// </summary>
    [HttpPost("{key}")]
    [RequirePermission("filterlists:write")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(BlockedServiceDetailExample))]
    public async Task<ActionResult<BlockedServiceDetail>> Enable(
        string key,
        [FromQuery] Guid? clientId,
        [FromQuery] Guid? clientGroupId,
        CancellationToken stoppingToken
    )
    {
        BlockedServiceCatalogEntry entry = BlockedServiceCatalog.FindByKey(key);
        if (entry is null)
            throw new NotFoundException("BlockedService", key);

        await ValidateTargetAsync(clientId, clientGroupId, stoppingToken);

        IReadOnlyList<DnsFilterListEntity> managedLists = await ListManagedAsync(stoppingToken);
        DnsFilterListEntity existing = FindManagedList(entry, managedLists);
        DnsFilterListEntity list;

        if (existing is not null)
        {
            if (existing.IsDisabled)
            {
                existing.IsDisabled = false;
                existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
                filterListRepository.Update(existing);
            }

            list = existing;
        }
        else
        {
            // A plain "Blocked Service: <name>" list name could collide with an operator's own,
            // unrelated list of the exact same name -- rather than silently reusing (and
            // potentially hijacking) it, generate a name guaranteed unique the same way the
            // ordinary Filter Lists create form itself would reject a real collision, by checking
            // first.
            string listName = $"Blocked Service: {entry.DisplayName}";
            if (await filterListRepository.FindByNameAsync(listName, stoppingToken) is not null)
                listName = $"Blocked Service: {entry.DisplayName} ({entry.Key})";

            list = new DnsFilterListEntity
            {
                Id = Guid.NewGuid(),
                Name = listName,
                Kind = DnsFilterListKindEnum.Domain,
                Action = DnsFilterListActionEnum.Block,
                Source = DnsFilterListSourceEnum.Subscribed,
                SubscriptionUrl = entry.SubscriptionUrl,
                RefreshInterval = RefreshInterval,
                IsDisabled = false,
                Comments =
                    $"Managed by Ninlil's curated Blocked Services catalog (key: {entry.Key}). "
                    + "Source: The Block List Project (https://github.com/blocklistproject/Lists), "
                    + "Unlicense/MIT. Do not rely on this list's own name to identify it -- toggle "
                    + "it off from the Blocked Services page instead of deleting it here.",
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };

            await filterListRepository.AddAsync(list, stoppingToken);
        }

        await EnsureAssignmentAsync(list.Id, clientId, clientGroupId, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        return Ok(await ToDetailAsync(entry, list, clientId, clientGroupId, stoppingToken));
    }

    /// <summary>
    ///     This action disables a single catalog entry. With neither target given, this keeps
    ///     doing exactly what it always has: permanently deletes its own managed
    ///     <see cref="DnsFilterListEntity" /> and every client-group <em>and</em> (now that one can
    ///     exist at all, see this class's own doc comment) client-level assignment still
    ///     referencing it, mirroring <see cref="FilterListsController.Delete" />'s own identical
    ///     cleanup. With a single optional <c>clientId</c>/<c>clientGroupId</c> target given, this
    ///     instead removes <em>only that target's own assignment</em>, leaving the shared managed
    ///     list -- and every other target's own assignment -- completely untouched: an operator
    ///     un-blocking a service for one specific child's device must not also silently un-block it
    ///     for everyone else who has it assigned. A no-op, not an error, when there is nothing to
    ///     remove in the given scope (the entry has no managed list at all, or the named target
    ///     simply has no assignment of its own).
    /// </summary>
    [HttpDelete("{key}")]
    [RequirePermission("filterlists:write")]
    public async Task<IActionResult> Disable(
        string key,
        [FromQuery] Guid? clientId,
        [FromQuery] Guid? clientGroupId,
        CancellationToken stoppingToken
    )
    {
        BlockedServiceCatalogEntry entry = BlockedServiceCatalog.FindByKey(key);
        if (entry is null)
            throw new NotFoundException("BlockedService", key);

        ValidateMutuallyExclusive(clientId, clientGroupId);

        IReadOnlyList<DnsFilterListEntity> managedLists = await ListManagedAsync(stoppingToken);
        DnsFilterListEntity existing = FindManagedList(entry, managedLists);

        if (existing is null)
            return NoContent();

        if (clientId is { } id)
        {
            DnsClientListAssignmentEntity assignment = (
                await clientListAssignmentRepository.ListByClientIdAsync(id, stoppingToken)
            ).FirstOrDefault(candidate => candidate.ListId == existing.Id);

            if (assignment is not null)
                clientListAssignmentRepository.Remove(assignment);
        }
        else if (clientGroupId is { } groupId)
        {
            DnsClientGroupListAssignmentEntity assignment = (
                await listAssignmentRepository.ListByGroupIdAsync(groupId, stoppingToken)
            ).FirstOrDefault(candidate => candidate.ListId == existing.Id);

            if (assignment is not null)
                listAssignmentRepository.Remove(assignment);
        }
        else
        {
            IReadOnlyList<DnsClientGroupListAssignmentEntity> groupAssignments =
                await listAssignmentRepository.ListAsync(
                    assignment => assignment.ListId == existing.Id,
                    stoppingToken
                );
            foreach (DnsClientGroupListAssignmentEntity assignment in groupAssignments)
                listAssignmentRepository.Remove(assignment);

            // A client-level assignment referencing this list could only ever exist via a prior,
            // targeted Enable call above -- cleaned up here too so the shared list's own removal
            // below never leaves a dangling reference behind.
            IReadOnlyList<DnsClientListAssignmentEntity> clientAssignments =
                await clientListAssignmentRepository.ListAsync(
                    assignment => assignment.ListId == existing.Id,
                    stoppingToken
                );
            foreach (DnsClientListAssignmentEntity assignment in clientAssignments)
                clientListAssignmentRepository.Remove(assignment);

            filterListRepository.Remove(existing);
        }

        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This method rejects a request naming both a client and a client-group target at once --
    ///     shared by every action that accepts the optional targeting pair.
    /// </summary>
    private static void ValidateMutuallyExclusive(Guid? clientId, Guid? clientGroupId)
    {
        if (clientId is not null && clientGroupId is not null)
            throw new ValidationException("'clientId' and 'clientGroupId' cannot both be given.");
    }

    /// <summary>
    ///     This method validates an <c>Enable</c> request's own optional targeting: rejects both
    ///     being given at once, then confirms whichever one <em>is</em> given actually names an
    ///     existing row -- a write action must never silently create an assignment pointed at a
    ///     client/group that does not exist.
    /// </summary>
    private async Task ValidateTargetAsync(
        Guid? clientId,
        Guid? clientGroupId,
        CancellationToken stoppingToken
    )
    {
        ValidateMutuallyExclusive(clientId, clientGroupId);

        if (clientId is { } id && await clientRepository.GetByIdAsync(id, stoppingToken) is null)
            throw new ValidationException($"No client with id '{id}' exists.");

        if (
            clientGroupId is { } groupId
            && await clientGroupRepository.GetByIdAsync(groupId, stoppingToken) is null
        )
            throw new ValidationException($"No client group with id '{groupId}' exists.");
    }

    /// <summary>
    ///     This method creates a target's own assignment of the managed list, unless one already
    ///     exists -- a no-op, not an error, either way when no target is given at all (today's
    ///     exact no-target behavior: a managed list is created/re-enabled but never automatically
    ///     assigned to anything).
    /// </summary>
    private async Task EnsureAssignmentAsync(
        Guid listId,
        Guid? clientId,
        Guid? clientGroupId,
        CancellationToken stoppingToken
    )
    {
        if (clientId is { } id)
        {
            IReadOnlyList<DnsClientListAssignmentEntity> existing =
                await clientListAssignmentRepository.ListByClientIdAsync(id, stoppingToken);
            if (existing.Any(assignment => assignment.ListId == listId))
                return;

            await clientListAssignmentRepository.AddAsync(
                new DnsClientListAssignmentEntity
                {
                    Id = Guid.NewGuid(),
                    ClientId = id,
                    ListId = listId,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                },
                stoppingToken
            );
        }
        else if (clientGroupId is { } groupId)
        {
            IReadOnlyList<DnsClientGroupListAssignmentEntity> existing =
                await listAssignmentRepository.ListByGroupIdAsync(groupId, stoppingToken);
            if (existing.Any(assignment => assignment.ListId == listId))
                return;

            await listAssignmentRepository.AddAsync(
                new DnsClientGroupListAssignmentEntity
                {
                    Id = Guid.NewGuid(),
                    ClientGroupId = groupId,
                    ListId = listId,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                },
                stoppingToken
            );
        }
    }

    /// <summary>
    ///     This method loads every currently-known, <see cref="DnsFilterListSourceEnum.Subscribed" />
    ///     list this catalog could possibly have created -- a plain <see cref="IRepository{TEntity}.ListAsync" />
    ///     filter, matching this project's own established "acceptable at homelab/small-org scale"
    ///     reasoning (see <see cref="DnsRewritesController.FindConflictingAsync" /> for the same
    ///     precedent) rather than a dedicated repository lookup.
    /// </summary>
    private async Task<IReadOnlyList<DnsFilterListEntity>> ListManagedAsync(
        CancellationToken stoppingToken
    ) =>
        await filterListRepository.ListAsync(
            list => list.Source == DnsFilterListSourceEnum.Subscribed,
            stoppingToken
        );

    /// <summary>
    ///     This method finds a catalog entry's own managed list among an already-loaded set of
    ///     subscribed lists, matching by <see cref="DnsFilterListEntity.SubscriptionUrl" /> -- see
    ///     this class's own doc comment for why that, not <c>Name</c>, is the stable key.
    /// </summary>
    private static DnsFilterListEntity FindManagedList(
        BlockedServiceCatalogEntry entry,
        IReadOnlyList<DnsFilterListEntity> managedLists
    ) =>
        managedLists.FirstOrDefault(list =>
            string.Equals(
                list.SubscriptionUrl,
                entry.SubscriptionUrl,
                StringComparison.OrdinalIgnoreCase
            )
        );

    /// <summary>
    ///     This method builds <see cref="BlockedServiceDetail" /> for a catalog entry given its own
    ///     already-resolved managed list (or <see langword="null" /> if it has none) -- with
    ///     neither target given, <see cref="BlockedServiceDetail.IsEnabled" /> reflects the shared
    ///     list's own global state exactly as it always has; with one given, it instead reflects
    ///     whether that specific target currently has its own assignment of the list.
    /// </summary>
    private async Task<BlockedServiceDetail> ToDetailAsync(
        BlockedServiceCatalogEntry entry,
        DnsFilterListEntity managedList,
        Guid? clientId,
        Guid? clientGroupId,
        CancellationToken stoppingToken
    )
    {
        bool isEnabled;

        if (managedList is not { IsDisabled: false })
        {
            isEnabled = false;
        }
        else if (clientId is { } id)
        {
            isEnabled = (
                await clientListAssignmentRepository.ListByClientIdAsync(id, stoppingToken)
            ).Any(assignment => assignment.ListId == managedList.Id);
        }
        else if (clientGroupId is { } groupId)
        {
            isEnabled = (
                await listAssignmentRepository.ListByGroupIdAsync(groupId, stoppingToken)
            ).Any(assignment => assignment.ListId == managedList.Id);
        }
        else
        {
            isEnabled = true;
        }

        return new BlockedServiceDetail(
            entry.Key,
            entry.DisplayName,
            entry.Description,
            entry.SubscriptionUrl,
            isEnabled,
            managedList?.Id
        );
    }
}
