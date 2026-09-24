using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.Core.ClusterSync;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Shared;

/// <summary>
///     This class implements the sending-side shape every Phase 9-6c cluster-config-sync event
///     subscriber shares, once: on a local change, re-read the affected entity fresh (a create
///     or update) or synthesize a bare deletion envelope (there is nothing left to re-read), then
///     hand the resulting <see cref="ClusterConfigSyncEnvelope" /> to
///     <see cref="IClusterConfigSyncBroadcaster.BroadcastAsync" />. Mirrors
///     <c>CatalogZoneAutoProvisioningEventSubscriber</c>'s own "open a fresh scope, do the real
///     work in a detached background task, log rather than throw" shape exactly -- every concrete
///     subscriber (<c>FilterListClusterSyncEventSubscriber</c> and its four siblings) only ever
///     needs to supply <see cref="EntityType" />/<see cref="FindLocalAsync" />/<see cref="Serialize" />
///     and call <see cref="BroadcastChangeAsync" /> (detached, via a discarded task) from its own
///     <c>IDnsServerEventSubscriber{TEvent}.HandleAsync</c> implementation.
/// </summary>
/// <typeparam name="TEntity">The entity type this subscriber's own event concerns.</typeparam>
/// <param name="scopeFactory">The factory used to reach a fresh, scoped repository per event.</param>
/// <param name="broadcaster">The broadcaster this subscriber forwards a built envelope to.</param>
/// <param name="clusterOptions">This node's own cluster configuration, read for its own node id.</param>
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
    ///     This property contains the entity-type discriminator this subscriber's own envelopes
    ///     carry -- must exactly match the matching <c>IClusterConfigSyncApplier.EntityType</c>
    ///     on the receiving side.
    /// </summary>
    protected abstract string EntityType { get; }

    /// <summary>
    ///     This method looks up the entity a create/update event concerns, fresh, from a
    ///     newly-opened scope's own repository.
    /// </summary>
    /// <param name="services">The scope's own service provider, to resolve a repository from.</param>
    /// <param name="id">The entity's identifier.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The entity, or <see langword="null" /> if it no longer exists (a benign race with a near-simultaneous local delete).</returns>
    protected abstract Task<TEntity> FindLocalAsync(
        IServiceProvider services,
        Guid id,
        CancellationToken stoppingToken
    );

    /// <summary>
    ///     This method serializes an entity's own mutable fields into the JSON shape the matching
    ///     <c>IClusterConfigSyncApplier</c> implementation expects.
    /// </summary>
    protected abstract string Serialize(TEntity entity);

    /// <summary>
    ///     This method does the actual work of building and broadcasting one envelope, with every
    ///     exception (other than cancellation) caught and logged so a broadcast failure never
    ///     propagates back into <c>DnsServerEventDispatcher</c>. Called as a detached task from
    ///     each concrete subscriber's own <c>HandleAsync</c>, per this class's own doc comment.
    /// </summary>
    /// <param name="entityId">The identifier of the entity that changed.</param>
    /// <param name="isDeleted">Whether the change was a deletion.</param>
    /// <param name="occurredAtUtc">
    ///     The event's own timestamp -- used verbatim as the envelope's own
    ///     <see cref="ClusterConfigSyncEnvelope.LastModifiedUtc" /> only for a deletion, where
    ///     there is no longer a local row to re-read a timestamp from; a create/update instead
    ///     uses the freshly re-read entity's own <see cref="AuditableEntityBaseExtensions.GetLastModifiedUtc" />,
    ///     which is what the local write path itself actually stamped.
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
            Guid ownNodeId = clusterOptions.CurrentValue.NodeId;

            if (isDeleted)
            {
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

            // A benign race: the entity was already deleted again (or never existed) by the time
            // this detached task actually ran. Nothing to broadcast.
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
