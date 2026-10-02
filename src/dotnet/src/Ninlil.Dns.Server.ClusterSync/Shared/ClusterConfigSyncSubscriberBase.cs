using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.Core.ClusterSync;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Shared;

/// <summary>
///     This class is the sending-side flow shared by the simple config-sync event subscribers
///     (<c>FilterListClusterSyncEventSubscriber</c> and the four like it). On a local change it
///     reads the entity again in a new scope (create or update) or records a tombstone (see
///     <see cref="ClusterSyncTombstones" />) and builds a bare deletion envelope, and hands the <see cref="ClusterConfigSyncEnvelope" /> to
///     <see cref="IClusterConfigSyncBroadcaster.BroadcastAsync" />. A subclass supplies
///     <see cref="EntityType" />, <see cref="FindLocalAsync" /> and <see cref="Serialize" />, and
///     calls <see cref="BroadcastChangeAsync" /> on a detached task from its <c>HandleAsync</c>,
///     like <c>CatalogZoneAutoProvisioningEventSubscriber</c>, so the event dispatcher is not
///     held up.
/// </summary>
/// <typeparam name="TEntity">The entity type the subscriber's event concerns.</typeparam>
/// <param name="scopeFactory">The factory used to create a scope, and a repository, per event.</param>
/// <param name="broadcaster">The broadcaster this subscriber forwards a built envelope to.</param>
/// <param name="clusterOptions">The cluster settings, read for this node's id.</param>
/// <param name="logger">The logger a broadcast failure is recorded to.</param>
public abstract class ClusterConfigSyncSubscriberBase<TEntity>(
    IServiceScopeFactory scopeFactory,
    IClusterConfigSyncBroadcaster broadcaster,
    IOptionsMonitor<ClusterOptions> clusterOptions,
    ILogger logger
)
    where TEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains the entity type the envelopes carry, which must equal the
    ///     receiving applier's <c>IClusterConfigSyncApplier.EntityType</c>.
    /// </summary>
    protected abstract string EntityType { get; }

    /// <summary>
    ///     This method reads the entity a create or update event concerns from the repository of
    ///     a new scope.
    /// </summary>
    /// <param name="services">The scope's service provider, to resolve a repository from.</param>
    /// <param name="id">The entity's identifier.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The entity, or <see langword="null" /> if it was deleted in the meantime.</returns>
    protected abstract Task<TEntity> FindLocalAsync(
        IServiceProvider services,
        Guid id,
        CancellationToken stoppingToken
    );

    /// <summary>
    ///     This method serializes an entity's fields into the JSON the matching
    ///     <c>IClusterConfigSyncApplier</c> expects.
    /// </summary>
    protected abstract string Serialize(TEntity entity);

    /// <summary>
    ///     This method builds and broadcasts one envelope, logging any exception other than
    ///     cancellation, since it runs as a detached task that nothing awaits.
    /// </summary>
    /// <param name="entityId">The identifier of the entity that changed.</param>
    /// <param name="isDeleted">Whether the change was a deletion.</param>
    /// <param name="occurredAtUtc">
    ///     The event's timestamp, used as <see cref="ClusterConfigSyncEnvelope.LastModifiedUtc" />
    ///     only for a deletion, where no row is left to read. A create or update uses the
    ///     entity's <see cref="AuditableEntityBaseExtensions.GetLastModifiedUtc" />, the time the
    ///     local write recorded.
    /// </param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    protected async Task BroadcastChangeAsync(
        Guid entityId,
        bool isDeleted,
        DateTimeOffset occurredAtUtc,
        CancellationToken stoppingToken
    )
    {
        try
        {
            ClusterOptions options = clusterOptions.CurrentValue;
            Guid ownNodeId = options.NodeId;

            if (isDeleted)
            {
                // Remembered only while clustering is on; a single node has nobody to hear a
                // stale change from.
                if (options.Enable)
                    await ClusterSyncTombstones.RecordLocalDeletionAsync(
                        scopeFactory,
                        EntityType,
                        entityId,
                        occurredAtUtc,
                        ownNodeId,
                        stoppingToken
                    );

                await broadcaster.BroadcastAsync(
                    new ClusterConfigSyncEnvelope(
                        EntityType,
                        entityId.ToString(),
                        IsDeleted: true,
                        PayloadJson: null,
                        occurredAtUtc,
                        ownNodeId
                    ),
                    stoppingToken
                );
                return;
            }

            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            TEntity entity = await FindLocalAsync(scope.ServiceProvider, entityId, stoppingToken);

            // The entity was deleted before this task ran; the delete has its own event.
            if (entity is null)
                return;

            await broadcaster.BroadcastAsync(
                new ClusterConfigSyncEnvelope(
                    EntityType,
                    entityId.ToString(),
                    IsDeleted: false,
                    Serialize(entity),
                    entity.GetLastModifiedUtc(),
                    entity.OriginatingNodeId ?? ownNodeId
                ),
                stoppingToken
            );
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Unhandled exception while broadcasting a {EntityType} config-sync change for "
                    + "'{EntityId}'.",
                EntityType,
                entityId
            );
        }
    }
}
