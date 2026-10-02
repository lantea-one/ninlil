using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.ClusterSync;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Shared;

/// <summary>
///     This class holds the tombstone rules every config-sync applier and sending subscriber
///     shares (see <see cref="ClusterSyncTombstoneEntity" /> for why tombstones exist). A
///     deletion, local or incoming, is recorded; a create for a key with a tombstone is applied
///     only if it is newer than the deletion under
///     <see cref="ClusterSyncConflictResolver.IncomingWins" />, and then removes the tombstone.
///     The methods stage changes only; the caller saves them with its own unit of work.
/// </summary>
public static class ClusterSyncTombstones
{
    /// <summary>
    ///     This method decides whether an incoming create may go ahead for a key this node does not
    ///     have. When a newer create beats an older deletion, the tombstone is staged for removal.
    /// </summary>
    /// <param name="repository">The tombstone repository.</param>
    /// <param name="id">The entity's id, parsed from the envelope's key.</param>
    /// <param name="envelope">The incoming create or update.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     <see langword="true" /> if the entity may be created, <see langword="false" /> if it was
    ///     deleted later than this change was made.
    /// </returns>
    public static async Task<bool> AllowsCreateAsync(
        IClusterSyncTombstoneRepository repository,
        Guid id,
        ClusterConfigSyncEnvelope envelope,
        CancellationToken stoppingToken
    )
    {
        ClusterSyncTombstoneEntity tombstone = await repository.FindAsync(
            envelope.EntityType,
            id.ToString(),
            stoppingToken
        );
        if (tombstone is null)
            return true;

        if (
            !ClusterSyncConflictResolver.IncomingWins(
                tombstone.GetLastModifiedUtc(),
                tombstone.OriginatingNodeId,
                envelope.LastModifiedUtc,
                envelope.OriginatingNodeId
            )
        )
            return false;

        repository.Remove(tombstone);
        return true;
    }

    /// <summary>
    ///     This method stages a tombstone for a deletion, or moves an existing one forward if this
    ///     deletion is the later of the two.
    /// </summary>
    /// <param name="repository">The tombstone repository.</param>
    /// <param name="entityType">The synced entity type.</param>
    /// <param name="id">The deleted entity's id; its string form is the tombstone's key.</param>
    /// <param name="deletedAtUtc">When the deletion happened, on the node it happened on.</param>
    /// <param name="originatingNodeId">The node the deletion happened on.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    public static async Task RecordDeletionAsync(
        IClusterSyncTombstoneRepository repository,
        string entityType,
        Guid id,
        DateTimeOffset deletedAtUtc,
        Guid originatingNodeId,
        CancellationToken stoppingToken
    )
    {
        string entityKey = id.ToString();
        ClusterSyncTombstoneEntity tombstone = await repository.FindAsync(
            entityType,
            entityKey,
            stoppingToken
        );

        if (tombstone is null)
        {
            await repository.AddAsync(
                new ClusterSyncTombstoneEntity
                {
                    Id = Guid.NewGuid(),
                    EntityType = entityType,
                    EntityKey = entityKey,
                    CreatedAtUtc = deletedAtUtc,
                    OriginatingNodeId = originatingNodeId,
                },
                stoppingToken
            );
            return;
        }

        if (
            !ClusterSyncConflictResolver.IncomingWins(
                tombstone.GetLastModifiedUtc(),
                tombstone.OriginatingNodeId,
                deletedAtUtc,
                originatingNodeId
            )
        )
            return;

        tombstone.UpdatedAtUtc = deletedAtUtc;
        tombstone.OriginatingNodeId = originatingNodeId;
        repository.Update(tombstone);
    }

    /// <summary>
    ///     This method records a local deletion in its own scope and saves it, for the sending
    ///     subscribers, which run on a detached task after the delete has been committed. Without
    ///     it, a peer's older change to the same entity arriving here later would recreate it.
    /// </summary>
    /// <param name="scopeFactory">The factory used to create the scope the tombstone is saved in.</param>
    /// <param name="entityType">The synced entity type.</param>
    /// <param name="id">The deleted entity's id.</param>
    /// <param name="deletedAtUtc">When the deletion happened.</param>
    /// <param name="ownNodeId">This node's id.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    public static async Task RecordLocalDeletionAsync(
        IServiceScopeFactory scopeFactory,
        string entityType,
        Guid id,
        DateTimeOffset deletedAtUtc,
        Guid ownNodeId,
        CancellationToken stoppingToken
    )
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        await RecordDeletionAsync(
            scope.ServiceProvider.GetRequiredService<IClusterSyncTombstoneRepository>(),
            entityType,
            id,
            deletedAtUtc,
            ownNodeId,
            stoppingToken
        );
        await scope
            .ServiceProvider.GetRequiredService<IUnitOfWork>()
            .SaveChangesAsync(stoppingToken);
    }
}
