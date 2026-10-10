using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Filtering.BlockedServices;
using Ninlil.Web.Examples.BlockedServices;
using Ninlil.Web.Models;
using Ninlil.Web.Routing;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller serves the "blocked services" catalog (<c>BlockedServiceCatalog</c>):
///     one-click toggles for named consumer services, instead of building a filter list by hand.
///     It is a thin layer over the block/allow-list engine of <see cref="FilterListsController" />
///     and <see cref="FilterListEntriesController" />: enabling an entry creates (or re-enables)
///     one ordinary <c>Subscribed</c> <see cref="DnsFilterListEntity" /> pointing at the entry's
///     third-party domain list, and disabling it removes that list. <c>BlockedServiceCatalog</c>
///     documents the data source, license and scope. Reads need <c>filterlists:read</c> and
///     changes need <c>filterlists:write</c>, the Filter Lists permissions, since all it does is
///     manage ordinary filter lists.
///     <para>
///         <b>Finding an entry's list again.</b> The managed list is the subscribed list whose
///         <see cref="DnsFilterListEntity.SubscriptionUrl" /> equals the entry's
///         <see cref="BlockedServiceCatalogEntry.SubscriptionUrl" />. That survives a rename,
///         which matching by <c>Name</c> would not.
///     </para>
///     <para>
///         <b>Targeting a client or client group.</b> Every action takes optional, mutually
///         exclusive <c>clientId</c> and <c>clientGroupId</c> query parameters. The shared list is
///         created, re-enabled or found as above either way. With a target, <c>Enable</c> also
///         creates a
///         <see cref="Ninlil.Dns.Data.Abstractions.Entity.DnsClientListAssignmentEntity" /> or
///         <see cref="DnsClientGroupListAssignmentEntity" /> linking the list to that target,
///         through the repositories behind <see cref="ClientListAssignmentsController" /> and
///         <see cref="ClientGroupListAssignmentsController" />, saving a second call. With a
///         target, <c>Disable</c> removes only that target's assignment and leaves the list and
///         other targets alone. Without one, <c>Disable</c> deletes the list, its entries and
///         every client and client-group assignment that refers to it.
///     </para>
/// </summary>
/// <param name="filterListRepository">Reads and writes the managed filter lists.</param>
/// <param name="listAssignmentRepository">
///     Creates and removes client-group assignments, including the cleanup when a list is
///     deleted (as in <see cref="FilterListsController.Delete" />).
/// </param>
/// <param name="unitOfWork">Commits staged changes.</param>
/// <param name="clientRepository">Confirms a <c>clientId</c> target exists.</param>
/// <param name="clientGroupRepository">Confirms a <c>clientGroupId</c> target exists.</param>
/// <param name="clientListAssignmentRepository">Creates and removes client assignments.</param>
/// <param name="entryRepository">Removes the entries of a deleted list.</param>
[ApiController]
[Route("api/blocked-services")]
public class BlockedServicesController(
    IDnsFilterListRepository filterListRepository,
    IDnsClientGroupListAssignmentRepository listAssignmentRepository,
    IUnitOfWork unitOfWork,
    IDnsClientRepository clientRepository,
    IDnsClientGroupRepository clientGroupRepository,
    IDnsClientListAssignmentRepository clientListAssignmentRepository,
    IDnsFilterListEntryRepository entryRepository
) : ControllerBase
{
    /// <summary>
    ///     This constant contains how often an enabled entry's list is fetched again, the same
    ///     default <c>DnsServerPresetCatalog</c> uses for preset-owned subscribed lists.
    /// </summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(24);

    /// <summary>
    ///     This action lists every catalog entry with whether it is enabled: globally by default,
    ///     or for the given <c>clientId</c> or <c>clientGroupId</c> (not both, which returns 400).
    ///     Requires <c>filterlists:read</c>.
    /// </summary>
    [HttpGet]
    [ReadFromReplica]
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
    ///     This action enables one catalog entry: it creates the entry's managed
    ///     <see cref="DnsFilterListEntity" /> if none exists or re-enables a disabled one, and,
    ///     with a <c>clientId</c> or <c>clientGroupId</c>, also assigns the list to that target if
    ///     it is not already. Enabling an enabled entry just returns its state. Returns 400 when
    ///     both targets are given or the target does not exist, and 404 for an unknown key.
    ///     Requires <c>filterlists:write</c>.
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
            // "Blocked Service: <name>" could already be the name of an unrelated operator list.
            // Rather than reuse (and take over) that list, the key is added to make the name
            // unique.
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
    ///     This action disables one catalog entry. Without a target it deletes the entry's managed
    ///     <see cref="DnsFilterListEntity" />, its entries and every client and client-group
    ///     assignment that refers to it (as <see cref="FilterListsController.Delete" /> does). With a <c>clientId</c> or <c>clientGroupId</c> it removes only that
    ///     target's assignment and leaves the list and every other target alone, so unblocking a
    ///     service for one device does not unblock it for everyone. Having nothing to remove is not
    ///     an error. Returns 400 when both targets are given and 404 for an unknown key. Requires
    ///     <c>filterlists:write</c>.
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
            await DeleteManagedListAsync(existing, stoppingToken);
            return NoContent();
        }

        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This method deletes a managed list together with its entries and every client and
    ///     client-group assignment, as <see cref="FilterListsController.Delete" /> does. The
    ///     entry delete runs at once on the relational engines, so the removal sits in one
    ///     transaction there; MongoDB registers no transaction factory.
    /// </summary>
    private async Task DeleteManagedListAsync(
        DnsFilterListEntity list,
        CancellationToken stoppingToken
    )
    {
        IUnitOfWorkTransactionFactory transactionFactory =
            HttpContext.RequestServices.GetService<IUnitOfWorkTransactionFactory>();
        await using IUnitOfWorkTransaction transaction = transactionFactory is null
            ? null
            : await transactionFactory.BeginAsync(stoppingToken);

        await DnsFilterListDependents.StageRemovalAsync(
            list.Id,
            listAssignmentRepository,
            clientListAssignmentRepository,
            entryRepository,
            stoppingToken
        );
        filterListRepository.Remove(list);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        if (transaction is not null)
            await transaction.CommitAsync(stoppingToken);
    }

    /// <summary>
    ///     This method rejects a request that names both a client and a client group.
    /// </summary>
    private static void ValidateMutuallyExclusive(Guid? clientId, Guid? clientGroupId)
    {
        if (clientId is not null && clientGroupId is not null)
            throw new ValidationException("'clientId' and 'clientGroupId' cannot both be given.");
    }

    /// <summary>
    ///     This method checks an <c>Enable</c> request's target: not both kinds at once, and the
    ///     one given must exist, so no assignment points at a missing client or group.
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
    ///     This method assigns the managed list to the target unless it already is. Without a
    ///     target it does nothing; the list is created or re-enabled but not assigned to anyone.
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
    ///     This method loads every <see cref="DnsFilterListSourceEnum.Subscribed" /> list, among
    ///     which the catalog's managed lists are found, through
    ///     <see cref="IRepository{TEntity}.ListAsync" />.
    /// </summary>
    private async Task<IReadOnlyList<DnsFilterListEntity>> ListManagedAsync(
        CancellationToken stoppingToken
    ) =>
        await filterListRepository.ListAsync(
            list => list.Source == DnsFilterListSourceEnum.Subscribed,
            stoppingToken
        );

    /// <summary>
    ///     This method finds an entry's managed list among the loaded subscribed lists by
    ///     <see cref="DnsFilterListEntity.SubscriptionUrl" /> (see the class summary).
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
    ///     This method builds <see cref="BlockedServiceDetail" /> for an entry and its managed list
    ///     (or <see langword="null" />). Without a target,
    ///     <see cref="BlockedServiceDetail.IsEnabled" /> is whether the list exists and is enabled;
    ///     with one, whether that target also has an assignment of it.
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
