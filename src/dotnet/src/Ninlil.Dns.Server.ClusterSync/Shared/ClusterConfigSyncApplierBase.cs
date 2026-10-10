using Microsoft.Extensions.Logging;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.ClusterSync;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Shared;

/// <summary>
///     This class is the receiving-side flow shared by the simple
///     <see cref="IClusterConfigSyncApplier" /> implementations (filter lists, filter list
///     entries, access-control rules, rewrites, forwarders). <c>ClusterChannelController.ConfigSync</c>
///     calls <see cref="ApplyAsync" />, which parses <see cref="ClusterConfigSyncEnvelope.EntityKey" />
///     as the entity id, looks the row up, and creates, updates or deletes it when the incoming
///     change wins under <see cref="ClusterSyncConflictResolver.IncomingWins" />. A subclass
///     supplies only <see cref="CreateFromPayload" /> and <see cref="ApplyPayload" />, the JSON
///     shape of its entity's fields. The repositories are written directly, so applying a
///     change raises no change event and is not broadcast again. A deletion leaves a tombstone
///     (see <see cref="ClusterSyncTombstones" />), so an older create or update that arrives
///     after it does not bring the row back.
/// </summary>
/// <typeparam name="TEntity">The entity type this applier synchronizes.</typeparam>
/// <param name="repository">The repository this applier reads and writes the entity through.</param>
/// <param name="tombstoneRepository">The repository deletions are remembered through.</param>
/// <param name="unitOfWork">The unit of work every applied change is committed through.</param>
/// <param name="logger">The logger a malformed envelope is recorded to.</param>
/// <param name="transactionFactory">
///     When set, a winning deletion of a known row runs <see cref="OnRemovingAsync" />, the
///     removal, the tombstone and the save inside one transaction. An applier whose hook
///     changes data outside the change tracker passes it; the others leave it
///     <see langword="null" />.
/// </param>
public abstract class ClusterConfigSyncApplierBase<TEntity>(
    IRepository<TEntity> repository,
    IClusterSyncTombstoneRepository tombstoneRepository,
    IUnitOfWork unitOfWork,
    ILogger logger,
    IUnitOfWorkTransactionFactory transactionFactory = null
) : IClusterConfigSyncApplier
    where TEntity : AuditableEntityBase
{
    /// <inheritdoc />
    public abstract string EntityType { get; }

    /// <summary>
    ///     This method builds a new local entity from an envelope's payload, when this node does
    ///     not have the id yet. <see cref="ApplyAsync" /> sets <see cref="EntityBase.Id" />,
    ///     <see cref="EntityBase.CreatedAtUtc" />, <see cref="AuditableEntityBase.UpdatedAtUtc" />
    ///     and <see cref="AuditableEntityBase.OriginatingNodeId" /> on the returned instance
    ///     afterwards; an implementation fills only the entity's own fields from
    ///     <paramref name="payloadJson" />.
    /// </summary>
    /// <param name="id">The new entity's identifier, parsed from <see cref="ClusterConfigSyncEnvelope.EntityKey" />.</param>
    /// <param name="payloadJson">The entity's serialized field content.</param>
    /// <param name="envelope">The full envelope, for any implementation that needs more than just the payload.</param>
    protected abstract TEntity CreateFromPayload(
        Guid id,
        string payloadJson,
        ClusterConfigSyncEnvelope envelope
    );

    /// <summary>
    ///     This method overwrites an existing entity's fields from an envelope's payload, once the
    ///     incoming change has won. An implementation must not set
    ///     <see cref="AuditableEntityBase.UpdatedAtUtc" /> or
    ///     <see cref="AuditableEntityBase.OriginatingNodeId" />; the caller sets both afterwards.
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
    ///     This method can veto a create or update before it is staged (for example,
    ///     <c>FilterListEntryClusterConfigSyncApplier</c> skips an entry whose list does not exist
    ///     here yet). The default allows every write.
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

    /// <summary>
    ///     This method runs when a winning deletion is about to remove a known row, before
    ///     <c>repository.Remove</c>. An implementation removes the rows that refer to the entity
    ///     (no foreign key cascades to them). The default does nothing.
    /// </summary>
    /// <param name="local">The tracked local entity that is about to be removed.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    protected virtual Task OnRemovingAsync(TEntity local, CancellationToken stoppingToken) =>
        Task.CompletedTask;

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
            // A new id: a delete is only remembered, in case the create it follows is still on
            // its way; a create or update must not be older than such a deletion and must pass
            // CanApplyAsync.
            if (envelope.IsDeleted)
            {
                await RecordDeletionAsync(id, envelope, stoppingToken);
                await unitOfWork.SaveChangesAsync(stoppingToken);
                return;
            }

            if (
                !await ClusterSyncTombstones.AllowsCreateAsync(
                    tombstoneRepository,
                    id,
                    envelope,
                    stoppingToken
                )
            )
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

        // A known id: apply only if the incoming change is newer or wins the tie-break; a stale
        // or duplicate envelope is ignored.
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
            await using IUnitOfWorkTransaction transaction = transactionFactory is null
                ? null
                : await transactionFactory.BeginAsync(stoppingToken);

            await OnRemovingAsync(local, stoppingToken);
            repository.Remove(local);
            await RecordDeletionAsync(id, envelope, stoppingToken);
            await unitOfWork.SaveChangesAsync(stoppingToken);

            if (transaction is not null)
                await transaction.CommitAsync(stoppingToken);
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

    /// <summary>
    ///     This method stages the tombstone for an incoming deletion.
    /// </summary>
    private Task RecordDeletionAsync(
        Guid id,
        ClusterConfigSyncEnvelope envelope,
        CancellationToken stoppingToken
    ) =>
        ClusterSyncTombstones.RecordDeletionAsync(
            tombstoneRepository,
            EntityType,
            id,
            envelope.LastModifiedUtc,
            envelope.OriginatingNodeId,
            stoppingToken
        );
}
