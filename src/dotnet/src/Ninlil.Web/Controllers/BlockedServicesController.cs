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
///         <b>Targeting a client or client group, or everyone.</b> Every action takes optional,
///         mutually exclusive <c>clientId</c> and <c>clientGroupId</c> query parameters. The shared
///         list is created, re-enabled or found as above either way. With a target, <c>Enable</c>
///         also creates a
///         <see cref="Ninlil.Dns.Data.Abstractions.Entity.DnsClientListAssignmentEntity" /> or
///         <see cref="DnsClientGroupListAssignmentEntity" /> linking the list to that target,
///         through the repositories behind <see cref="ClientListAssignmentsController" /> and
///         <see cref="ClientGroupListAssignmentsController" />, saving a second call. With a
///         target, <c>Disable</c> removes only that target's assignment and leaves the list and
///         other targets alone. Without a target the toggle is global: <c>Enable</c> sets
///         <see cref="DnsFilterListEntity.AppliesToAllClients" />, which blocks the service for
///         every client, including one that no group or client record matches. Client and group
///         toggles add to it, so a client or group cannot unblock a service that is blocked
///         globally; the only per-client override is an allow list, which
///         <see cref="DnsFilterOptions.Precedence" /> weighs against the block as for any other
///         list. Without a target, <c>Disable</c> clears the flag and keeps the list and the
///         client and group assignments that still use it, so they block as before. When no
///         assignment is left it deletes the list and its entries as well.
///     </para>
///     <para>
///         <b>Cluster replication.</b> The managed list is an ordinary filter list, so creating or
///         re-enabling it stamps this node's id on it and publishes a
///         <see cref="DnsFilterListChangedEvent" />, and deleting it (untargeted <c>Disable</c>)
///         publishes the same event with <c>IsDeleted</c>; <c>FilterListClusterSyncEventSubscriber</c>
///         sends both to the peers, as for <see cref="FilterListsController" />. Client and
///         client-group assignments are node-local and never replicate, so a targeted
///         <c>Disable</c>, and an <c>Enable</c> that only adds an assignment, publish nothing.
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
/// <param name="eventPublisher">
///     Publishes <see cref="DnsFilterListChangedEvent" /> so the cluster-sync subscriber sends
///     the list change to every peer.
/// </param>
/// <param name="clusterOptions">
///     Supplies this node's cluster node id, stamped onto
///     <c>DnsFilterListEntity.OriginatingNodeId</c> when a list is created or re-enabled.
/// </param>
[ApiController]
[Route("api/blocked-services")]
public class BlockedServicesController(
    IDnsFilterListRepository filterListRepository,
    IDnsClientGroupListAssignmentRepository listAssignmentRepository,
    IUnitOfWork unitOfWork,
    IDnsClientRepository clientRepository,
    IDnsClientGroupRepository clientGroupRepository,
    IDnsClientListAssignmentRepository clientListAssignmentRepository,
    IDnsFilterListEntryRepository entryRepository,
    IDnsServerEventPublisher eventPublisher,
    IOptionsMonitor<ClusterOptions> clusterOptions
) : ControllerBase
{
    /// <summary>
    ///     This constant contains how often an enabled entry's list is fetched again, the same
    ///     default <c>DnsServerPresetCatalog</c> uses for preset-owned subscribed lists.
    /// </summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(24);

    /// <summary>
    ///     This action lists every catalog entry with whether it is enabled: for every client by
    ///     default, or for the given <c>clientId</c> or <c>clientGroupId</c> (not both, which
    ///     returns 400).
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
    ///     it is not already. Without a target it marks the list as applying to every client.
    ///     Enabling an enabled entry just returns its state. Returns 400 when
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
        bool listChanged = false;

        if (existing is not null)
        {
            if (existing.IsDisabled)
            {
                existing.IsDisabled = false;
                existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
                existing.OriginatingNodeId = clusterOptions.CurrentValue.NodeId;
                listChanged = true;
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
                OriginatingNodeId = clusterOptions.CurrentValue.NodeId,
                AppliesToAllClients = clientId is null && clientGroupId is null,
            };

            await filterListRepository.AddAsync(list, stoppingToken);
            listChanged = true;
        }

        // The flag is what makes a toggle without a target block every client; it replicates with
        // the list, so a change publishes like a new or re-enabled list does.
        if (clientId is null && clientGroupId is null && !list.AppliesToAllClients)
        {
            list.AppliesToAllClients = true;
            list.UpdatedAtUtc = DateTimeOffset.UtcNow;
            list.OriginatingNodeId = clusterOptions.CurrentValue.NodeId;
            listChanged = true;
            filterListRepository.Update(list);
        }

        await EnsureAssignmentAsync(list.Id, clientId, clientGroupId, stoppingToken);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        if (listChanged)
            eventPublisher.Publish(new DnsFilterListChangedEvent(DateTimeOffset.UtcNow, list.Id));

        return Ok(await ToDetailAsync(entry, list, clientId, clientGroupId, stoppingToken));
    }

    /// <summary>
    ///     This action disables one catalog entry. Without a target it clears
    ///     <see cref="DnsFilterListEntity.AppliesToAllClients" />, so the service is no longer
    ///     blocked for every client, and tells the cluster peers. The managed
    ///     <see cref="DnsFilterListEntity" /> stays while a client or client group is still
    ///     assigned to it; when none is, the action deletes the list and its entries (as
    ///     <see cref="FilterListsController.Delete" /> does) and tells the peers to delete it. With
    ///     a <c>clientId</c> or
    ///     <c>clientGroupId</c> it removes only that target's assignment, which stays on this node,
    ///     and leaves the list and every other target alone, so unblocking a service for one
    ///     device does not unblock it for everyone. Having nothing to remove is not
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
            await ClearGlobalToggleAsync(existing, stoppingToken);
            return NoContent();
        }

        await unitOfWork.SaveChangesAsync(stoppingToken);

        return NoContent();
    }

    /// <summary>
    ///     This method turns the global toggle off: it clears the flag, then deletes the list when
    ///     no client or client group still uses it, and otherwise saves and publishes the changed
    ///     list. A list that never had the flag (a global toggle that predates it, assigned to
    ///     nobody) has nothing to clear and is deleted when unused, as before.
    /// </summary>
    private async Task ClearGlobalToggleAsync(
        DnsFilterListEntity list,
        CancellationToken stoppingToken
    )
    {
        bool isUsed =
            (
                await listAssignmentRepository.ListAsync(
                    assignment => assignment.ListId == list.Id,
                    stoppingToken
                )
            ).Count > 0
            || (
                await clientListAssignmentRepository.ListAsync(
                    assignment => assignment.ListId == list.Id,
                    stoppingToken
                )
            ).Count > 0;

        if (!isUsed)
        {
            await DeleteManagedListAsync(list, stoppingToken);
            return;
        }

        if (!list.AppliesToAllClients)
            return;

        list.AppliesToAllClients = false;
        list.UpdatedAtUtc = DateTimeOffset.UtcNow;
        list.OriginatingNodeId = clusterOptions.CurrentValue.NodeId;
        filterListRepository.Update(list);
        await unitOfWork.SaveChangesAsync(stoppingToken);

        eventPublisher.Publish(new DnsFilterListChangedEvent(DateTimeOffset.UtcNow, list.Id));
    }

    /// <summary>
    ///     This method deletes a managed list together with its entries and every client and
    ///     client-group assignment, as <see cref="FilterListsController.Delete" /> does. The
    ///     entry delete runs at once on the relational engines, so the removal sits in one
    ///     transaction there; MongoDB registers no transaction factory. The delete event is
    ///     published only after the commit.
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

        eventPublisher.Publish(
            new DnsFilterListChangedEvent(DateTimeOffset.UtcNow, list.Id, IsDeleted: true)
        );
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
    ///     target it does nothing; <see cref="Enable" /> marks the list as applying to every client
    ///     instead.
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
    ///     <see cref="BlockedServiceDetail.IsEnabled" /> is whether the list is enabled and
    ///     applies to every client; with one, whether that target has an assignment of it.
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
            isEnabled = managedList.AppliesToAllClients;
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
