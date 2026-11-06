using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.Core.ClusterSync;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Shared;

/// <summary>
///     This class is the <see cref="IClusterConfigSyncSource" /> for tombstones: it turns each
///     remembered deletion into the deletion envelope live sync sent for it, so a node that was
///     not there when the row was deleted removes its own copy. It is the last stage of a
///     backfill (<see cref="ClusterConfigBackfillStages.Tombstones" />), after every row an
///     earlier stage sent, and the receiving applier keeps a row that was changed after the
///     deletion, as last write wins.
///     <para>
///         Only a tombstone whose entity type has a registered
///         <see cref="IClusterConfigSyncApplier" /> is sent: the receiver would ignore the rest.
///         The envelope carries the tombstone's own time and node, never the time of the read.
///     </para>
/// </summary>
/// <param name="repository">The tombstone repository.</param>
/// <param name="appliers">The registered appliers, whose entity types say what a peer can apply.</param>
/// <param name="clusterOptions">The cluster settings, read for this node's id.</param>
public sealed class TombstoneClusterConfigSyncSource(
    IClusterSyncTombstoneRepository repository,
    IEnumerable<IClusterConfigSyncApplier> appliers,
    IOptionsMonitor<ClusterOptions> clusterOptions
) : IClusterConfigSyncSource
{
    /// <summary>
    ///     This constant is the entity type this source reports, since its envelopes carry the
    ///     type of the deleted entity instead.
    /// </summary>
    public const string EntityTypeName = "Tombstone";

    /// <inheritdoc />
    public string EntityType => EntityTypeName;

    /// <inheritdoc />
    public int BackfillStage => ClusterConfigBackfillStages.Tombstones;

    /// <inheritdoc />
    public async IAsyncEnumerable<ClusterConfigSyncEnvelope> ReadBackfillAsync(
        [EnumeratorCancellation] CancellationToken stoppingToken
    )
    {
        string[] appliedTypes = [.. appliers.Select(applier => applier.EntityType).Distinct()];
        IReadOnlyList<Guid> ids = await repository.ListIdsAsync(
            tombstone => appliedTypes.Contains(tombstone.EntityType),
            stoppingToken
        );
        Guid ownNodeId = clusterOptions.CurrentValue.NodeId;

        foreach (Guid id in ids)
        {
            stoppingToken.ThrowIfCancellationRequested();

            // A tombstone is removed when a newer create of its key wins; skip one removed since
            // the snapshot.
            ClusterSyncTombstoneEntity tombstone = await repository.GetByIdAsync(id, stoppingToken);
            if (tombstone is null)
                continue;

            yield return ClusterConfigSyncEnvelopes.ForDeletion(
                tombstone.EntityType,
                tombstone.EntityKey,
                tombstone.GetLastModifiedUtc(),
                tombstone.OriginatingNodeId ?? ownNodeId
            );
        }
    }
}
