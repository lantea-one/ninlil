using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.Core.ClusterSync;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Shared;

/// <summary>
///     This class is the sending-side flow shared by the config-sync event subscribers (filter
///     lists, entries, access-control rules, rewrites, forwarders, users and roles). On a local
///     change it records a tombstone (see <see cref="ClusterSyncTombstones" />) and builds a bare
///     deletion envelope, or, for a create or update, reads the entity again in a new scope and
///     asks the entity's <see cref="ClusterConfigSyncSourceBase{TEntity}" /> for the envelope. It
///     hands the <see cref="ClusterConfigSyncEnvelope" /> to
///     <see cref="IClusterConfigSyncBroadcaster.BroadcastAsync" />. The payload, the stamping and
///     the filter of what syncs live only in the source, which a backfill reads too, so a live
///     change and a backfill produce the same bytes.
///     <para>
///         A subclass supplies <see cref="EntityType" /> and calls <see cref="BroadcastChangeAsync" />
///         on a detached task from its <c>HandleAsync</c>, like
///         <c>CatalogZoneAutoProvisioningEventSubscriber</c>, so the event dispatcher is not held
///         up. The source for <typeparamref name="TEntity" /> must be registered as
///         <c>ClusterConfigSyncSourceBase&lt;TEntity&gt;</c> (see
///         <c>ServiceCollectionClusterSyncExtension</c>).
///     </para>
/// </summary>
/// <typeparam name="TEntity">The entity type the subscriber's event concerns.</typeparam>
/// <param name="scopeFactory">The factory used to create a scope, and the source, per event.</param>
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
    ///     receiving applier's <c>IClusterConfigSyncApplier.EntityType</c> and the source's
    ///     <see cref="IClusterConfigSyncSource.EntityType" />.
    /// </summary>
    protected abstract string EntityType { get; }

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
                    ClusterConfigSyncEnvelopes.ForDeletion(
                        EntityType,
                        entityId.ToString(),
                        occurredAtUtc,
                        ownNodeId
                    ),
                    stoppingToken
                );
                return;
            }

            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            ClusterConfigSyncEnvelope envelope = await scope
                .ServiceProvider.GetRequiredService<ClusterConfigSyncSourceBase<TEntity>>()
                .BuildLiveEnvelopeAsync(entityId, ownNodeId, stoppingToken);

            // The entity was deleted before this task ran (the delete has its own event), or it
            // is not one that syncs, such as a rewrite scoped to a client.
            if (envelope is null)
                return;

            await broadcaster.BroadcastAsync(envelope, stoppingToken);
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
