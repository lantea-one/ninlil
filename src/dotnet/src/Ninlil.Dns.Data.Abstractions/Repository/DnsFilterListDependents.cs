using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Repository;

/// <summary>
///     This class holds the one rule for what deleting a filter list removes besides the list
///     row: its entries, its client-group assignments and its direct client assignments. No
///     foreign key cascades to these rows, so every delete path calls it:
///     <c>FilterListsController.Delete</c>, the untargeted <c>BlockedServicesController.Disable</c>
///     and the cluster applier that applies a peer's delete of a list.
/// </summary>
public static class DnsFilterListDependents
{
    /// <summary>
    ///     This method stages the removal of every assignment that names the list and removes
    ///     the list's entries. The caller removes the list itself and saves. On the relational
    ///     engines the entry delete runs in the database at once, outside the change tracker,
    ///     so the caller must hold an <see cref="IUnitOfWorkTransaction" /> from
    ///     <see cref="IUnitOfWorkTransactionFactory" /> when one is registered (the rule of
    ///     <see cref="IDnsFilterListEntryRepository.ReplaceEntriesAsync" />) and commit it after
    ///     the save. MongoDB stages the entry delete and runs it at save time, outside the
    ///     session transaction.
    /// </summary>
    /// <param name="listId">The identifier of the list being deleted.</param>
    /// <param name="groupAssignments">Finds and removes the client-group assignments.</param>
    /// <param name="clientAssignments">Finds and removes the direct client assignments.</param>
    /// <param name="entries">Removes the list's entries.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    public static async Task StageRemovalAsync(
        Guid listId,
        IDnsClientGroupListAssignmentRepository groupAssignments,
        IDnsClientListAssignmentRepository clientAssignments,
        IDnsFilterListEntryRepository entries,
        CancellationToken stoppingToken = default
    )
    {
        foreach (
            DnsClientGroupListAssignmentEntity assignment in await groupAssignments.ListAsync(
                candidate => candidate.ListId == listId,
                stoppingToken
            )
        )
            groupAssignments.Remove(assignment);

        foreach (
            DnsClientListAssignmentEntity assignment in await clientAssignments.ListAsync(
                candidate => candidate.ListId == listId,
                stoppingToken
            )
        )
            clientAssignments.Remove(assignment);

        await entries.ReplaceEntriesAsync(listId, [], stoppingToken);
    }
}
