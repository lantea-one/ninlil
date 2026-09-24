using Microsoft.Extensions.Logging;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.ClusterSync;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Shared;

/// <summary>
///     This class implements the one real-work shape every entity-row
///     <see cref="IClusterConfigSyncApplier" /> in this project shares, once: parse the
///     envelope's own <see cref="ClusterConfigSyncEnvelope.EntityKey" /> as the entity's
///     <see cref="Guid" /> id, look it up locally, decide whether the incoming change actually
///     wins via <see cref="ClusterSyncConflictResolver.IncomingWins" />, and either create,
///     update, or delete the local row accordingly. Every concrete applier
///     (<c>FilterListClusterConfigSyncApplier</c>, <c>AccessControlRuleClusterConfigSyncApplier</c>,
///     <c>RewriteClusterConfigSyncApplier</c>, <c>ForwarderClusterConfigSyncApplier</c>,
///     <c>FilterListEntryClusterConfigSyncApplier</c>) only ever needs to supply
///     <see cref="CreateFromPayload" />/<see cref="ApplyPayload" /> -- the JSON shape of its own
///     entity's mutable fields -- never re-implement the lookup/compare/write flow itself.
/// </summary>
/// <typeparam name="TEntity">The entity type this applier synchronizes.</typeparam>
/// <param name="repository">The repository this applier reads and writes the entity through.</param>
/// <param name="unitOfWork">The unit of work every applied change is committed through.</param>
/// <param name="logger">The logger a malformed/rejected envelope is recorded to.</param>
public abstract class ClusterConfigSyncApplierBase<TEntity>(
    IRepository<TEntity> repository,
    IUnitOfWork unitOfWork,
    ILogger logger
) : IClusterConfigSyncApplier
    where TEntity : AuditableEntityBase
{
    /// <inheritdoc />
    public abstract string EntityType { get; }

    /// <summary>
    ///     This method builds a brand-new local entity from an incoming envelope's own payload,
    ///     for the case where this node has never seen this entity id before. The caller (this
    ///     base class's own <see cref="ApplyAsync" />) has already set <see cref="EntityBase.Id" />
    ///     /<see cref="EntityBase.CreatedAtUtc" />/<see cref="AuditableEntityBase.UpdatedAtUtc" />/
    ///     <see cref="AuditableEntityBase.OriginatingNodeId" /> correctly on the returned instance
    ///     before it is persisted; an implementation only needs to populate its own entity-specific
    ///     fields from <paramref name="payloadJson" />.
    /// </summary>
    /// <param name="id">The new entity's own identifier -- <paramref name="envelope" />'s own <see cref="ClusterConfigSyncEnvelope.EntityKey" />, already parsed.</param>
    /// <param name="payloadJson">The entity's serialized field content.</param>
    /// <param name="envelope">The full envelope, for any implementation that needs more than just the payload.</param>
    protected abstract TEntity CreateFromPayload(
        Guid id,
        string payloadJson,
        ClusterConfigSyncEnvelope envelope
    );

    /// <summary>
    ///     This method overwrites an already-existing local entity's own entity-specific fields
    ///     from an incoming envelope's payload -- called only once this base class has already
    ///     decided the incoming change wins. An implementation must not touch
    ///     <see cref="AuditableEntityBase.UpdatedAtUtc" />/<see cref="AuditableEntityBase.OriginatingNodeId" />
    ///     itself; the caller sets both immediately afterward.
    /// </summary>
    /// <param name="local">The already-tracked local entity to update in place.</param>
    /// <param name="payloadJson">The entity's serialized field content.</param>
    /// <param name="envelope">The full envelope, for any implementation that needs more than just the payload.</param>
    protected abstract void ApplyPayload(
        TEntity local,
        string payloadJson,
        ClusterConfigSyncEnvelope envelope
    );

    /// <summary>
    ///     This method optionally validates that a create/update is actually applicable before it
    ///     is staged (for example, <c>FilterListEntryClusterConfigSyncApplier</c> uses this to
    ///     skip an entry whose own owning list does not exist locally yet). The default
    ///     implementation always allows the write.
    /// </summary>
    /// <param name="payloadJson">The entity's serialized field content.</param>
    /// <param name="envelope">The full envelope.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns><see langword="true" /> if the write should proceed, <see langword="false" /> to skip it silently.</returns>
    protected virtual Task<bool> CanApplyAsync(
        string payloadJson,
        ClusterConfigSyncEnvelope envelope,
        CancellationToken stoppingToken
    ) => Task.FromResult(true);

    /// <inheritdoc />
    public async Task ApplyAsync(
        ClusterConfigSyncEnvelope envelope,
        CancellationToken stoppingToken
    )
    {
        if (!Guid.TryParse(envelope.EntityKey, out Guid id))
        {
            logger.LogWarning(
                "Received a {EntityType} config-sync envelope with a non-GUID entity key "
                    + "'{EntityKey}' -- ignored.",
                envelope.EntityType,
                envelope.EntityKey
            );
            return;
        }

        TEntity local = await repository.GetByIdAsync(id, stoppingToken);

        if (local is null)
        {
            // We have never seen this entity before. A delete for something we never had is
            // already a no-op; a create/update needs this base class's own CanApplyAsync check
            // (for example, an entry whose own owning list has not arrived yet) before it stages
            // a brand-new row.
            if (envelope.IsDeleted)
                return;

            if (!await CanApplyAsync(envelope.PayloadJson, envelope, stoppingToken))
                return;

            TEntity created = CreateFromPayload(id, envelope.PayloadJson, envelope);
            created.Id = id;
            created.CreatedAtUtc = envelope.LastModifiedUtc;
            created.UpdatedAtUtc = null;
            created.OriginatingNodeId = envelope.OriginatingNodeId;

            await repository.AddAsync(created, stoppingToken);
            await unitOfWork.SaveChangesAsync(stoppingToken);
            return;
        }

        // We already have this entity -- only overwrite it if the incoming change is genuinely
        // newer (or wins the deterministic tie-break); a stale envelope (for example, one that
        // arrived out of order, or a duplicate re-delivery) is silently discarded.
        bool incomingWins = ClusterSyncConflictResolver.IncomingWins(
            local.GetLastModifiedUtc(),
            local.OriginatingNodeId,
            envelope.LastModifiedUtc,
            envelope.OriginatingNodeId
        );

        if (!incomingWins)
            return;

        if (envelope.IsDeleted)
        {
            repository.Remove(local);
            await unitOfWork.SaveChangesAsync(stoppingToken);
            return;
        }

        if (!await CanApplyAsync(envelope.PayloadJson, envelope, stoppingToken))
            return;

        ApplyPayload(local, envelope.PayloadJson, envelope);
        local.UpdatedAtUtc = envelope.LastModifiedUtc;
        local.OriginatingNodeId = envelope.OriginatingNodeId;

        repository.Update(local);
        await unitOfWork.SaveChangesAsync(stoppingToken);
    }
}
