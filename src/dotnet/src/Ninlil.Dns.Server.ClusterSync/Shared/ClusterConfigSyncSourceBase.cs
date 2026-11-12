using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.Core.ClusterSync;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Shared;

/// <summary>
///     This class is the one place a synced entity type turns its rows into
///     <see cref="ClusterConfigSyncEnvelope" /> values. A subclass supplies the entity type, the
///     stage, the payload and, where live sync leaves rows out, the filter. Two callers use it:
///     <see cref="ClusterConfigSyncSubscriberBase{TEntity}" /> builds the envelope of one changed
///     row through <see cref="BuildLiveEnvelopeAsync" />, and a backfill reads every row through
///     <see cref="ReadBackfillAsync" />. Both stamp through <see cref="ClusterConfigSyncEnvelopes" />
///     and apply <see cref="SnapshotPredicate" />, so a peer cannot tell them apart.
///     <para>
///         The class is registered scoped, because it uses a scoped repository, both as itself
///         (the subscribers resolve it by type in their own scope) and as
///         <see cref="IClusterConfigSyncSource" />.
///     </para>
/// </summary>
/// <typeparam name="TEntity">The entity type the source reads.</typeparam>
/// <param name="repository">The repository the rows are read from.</param>
/// <param name="clusterOptions">The cluster settings, read for this node's id.</param>
/// <param name="logger">The logger a skipped row is reported to.</param>
public abstract class ClusterConfigSyncSourceBase<TEntity>(
    IRepository<TEntity> repository,
    IOptionsMonitor<ClusterOptions> clusterOptions,
    ILogger logger
) : IClusterConfigSyncSource
    where TEntity : AuditableEntityBase
{
    /// <summary>
    ///     This field contains <see cref="SnapshotPredicate" /> compiled once, to apply the same
    ///     rule to a single row that the database applies to the id snapshot.
    /// </summary>
    private Func<TEntity, bool> _includes;

    /// <inheritdoc />
    public abstract string EntityType { get; }

    /// <inheritdoc />
    public abstract int BackfillStage { get; }

    /// <summary>
    ///     This property contains the rule that selects the rows of this type that sync, or
    ///     <see langword="null" /> when every row does. It is written as an expression so the
    ///     database evaluates it for the id snapshot; a live change to a row it rejects sends
    ///     nothing. A rewrite scoped to a client or group is rejected, for example: the payload
    ///     has no scope and clients exist only on their own node, so a peer would create a global
    ///     rewrite from it.
    /// </summary>
    protected virtual Expression<Func<TEntity, bool>> SnapshotPredicate => null;

    /// <summary>
    ///     This method serializes a row's fields into the JSON the matching
    ///     <see cref="IClusterConfigSyncApplier" /> expects. It is asynchronous because a user or
    ///     role also reads its memberships and claims. A secret is decrypted here, and a
    ///     <see cref="CryptographicException" /> means this node cannot read it.
    /// </summary>
    /// <param name="entity">The row.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The payload JSON.</returns>
    protected abstract ValueTask<string> SerializeAsync(
        TEntity entity,
        CancellationToken stoppingToken
    );

    /// <summary>
    ///     This method builds the envelope of one row, whatever the filter says about it.
    /// </summary>
    /// <param name="entity">The row.</param>
    /// <param name="ownNodeId">This node's id.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The envelope, stamped like every other envelope of this type.</returns>
    public async Task<ClusterConfigSyncEnvelope> BuildEnvelopeAsync(
        TEntity entity,
        Guid ownNodeId,
        CancellationToken stoppingToken
    ) =>
        ClusterConfigSyncEnvelopes.ForEntity(
            EntityType,
            entity,
            await SerializeAsync(entity, stoppingToken),
            ownNodeId
        );

    /// <summary>
    ///     This method builds the envelope for a live create or update: it reads the row again,
    ///     because the event carries only an id.
    /// </summary>
    /// <param name="id">The changed row's id.</param>
    /// <param name="ownNodeId">This node's id.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     The envelope, or <see langword="null" /> when the row was deleted in the meantime (the
    ///     delete has its own event) or the row does not sync (see
    ///     <see cref="SnapshotPredicate" />). An exception from <see cref="SerializeAsync" />
    ///     propagates; the live subscriber logs it.
    /// </returns>
    public async Task<ClusterConfigSyncEnvelope> BuildLiveEnvelopeAsync(
        Guid id,
        Guid ownNodeId,
        CancellationToken stoppingToken
    )
    {
        TEntity entity = await repository.GetByIdAsync(id, stoppingToken);
        if (entity is null || !Includes(entity))
            return null;

        return await BuildEnvelopeAsync(entity, ownNodeId, stoppingToken);
    }

    /// <inheritdoc />
    public virtual async IAsyncEnumerable<ClusterConfigSyncEnvelope> ReadBackfillAsync(
        [EnumeratorCancellation] CancellationToken stoppingToken
    )
    {
        IReadOnlyList<Guid> ids = await repository.ListIdsAsync(SnapshotPredicate, stoppingToken);

        await foreach (ClusterConfigSyncEnvelope envelope in ReadRowsAsync(ids, stoppingToken))
            yield return envelope;
    }

    /// <summary>
    ///     This method loads each row of an id snapshot and builds its envelope. It skips a row
    ///     that is gone or no longer syncs (it changed after the snapshot), and a row whose secret
    ///     this node cannot decrypt: retrying cannot fix that, so the row is reported and the read
    ///     goes on, rather than one damaged account stopping the whole backfill.
    /// </summary>
    /// <param name="ids">The snapshot of ids to read.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>One envelope per row that is still there and readable.</returns>
    protected async IAsyncEnumerable<ClusterConfigSyncEnvelope> ReadRowsAsync(
        IReadOnlyList<Guid> ids,
        [EnumeratorCancellation] CancellationToken stoppingToken
    )
    {
        Guid ownNodeId = clusterOptions.CurrentValue.NodeId;

        foreach (Guid id in ids)
        {
            stoppingToken.ThrowIfCancellationRequested();

            TEntity entity = await repository.GetByIdAsync(id, stoppingToken);
            if (entity is null || !Includes(entity))
                continue;

            ClusterConfigSyncEnvelope envelope;
            try
            {
                envelope = await BuildEnvelopeAsync(entity, ownNodeId, stoppingToken);
            }
            catch (CryptographicException exception)
            {
                logger.LogWarning(
                    exception,
                    "Skipping the {EntityType} '{EntityId}' in the config backfill: a secret of "
                        + "it cannot be decrypted with this node's keys.",
                    EntityType,
                    id
                );
                continue;
            }

            yield return envelope;
        }
    }

    private bool Includes(TEntity entity)
    {
        if (SnapshotPredicate is null)
            return true;

        _includes ??= SnapshotPredicate.Compile();
        return _includes(entity);
    }
}
