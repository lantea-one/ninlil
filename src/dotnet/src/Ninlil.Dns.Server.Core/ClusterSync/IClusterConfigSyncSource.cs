// Define our namespace.
namespace Ninlil.Dns.Server.Core.ClusterSync;

/// <summary>
///     This interface is the reading half of cluster config sync, implemented once per synced
///     category and once for tombstones, beside the <see cref="IClusterConfigSyncApplier" /> that
///     receives the same category. A source lists everything this node holds of its category as
///     <see cref="ClusterConfigSyncEnvelope" /> values, so a node that is owed the full config
///     (a backfill to a peer that just joined) gets exactly what live sync sends for a change:
///     the same payload, the same stamping, the same filters. The live sending subscribers build
///     their envelopes through the same source, so the two paths cannot drift apart.
///     <para>
///         A caller resolves every registered source in one scope, groups them by
///         <see cref="BackfillStage" />, and sends the stages in ascending order so a receiver
///         already holds what a later item depends on (a user needs its roles, a list entry needs
///         its list). Within a stage the order does not matter: the receiving appliers decide each
///         conflict by last write wins and drop a repeat.
///     </para>
///     <para>
///         Secrets travel as plaintext, decrypted with this node's key ring, because ciphertext
///         is useless to a node with another key ring. That is safe only inside the pinned-TLS,
///         signed cluster channel, and the receiver encrypts them again with its own key ring.
///         A source never emits ciphertext.
///     </para>
/// </summary>
public interface IClusterConfigSyncSource
{
    /// <summary>
    ///     This property contains the entity type of the envelopes this source yields, equal to
    ///     the receiving <see cref="IClusterConfigSyncApplier.EntityType" />. The tombstone source
    ///     is the exception: its envelopes carry the type of the deleted entity, and this property
    ///     contains <c>"Tombstone"</c>.
    /// </summary>
    string EntityType { get; }

    /// <summary>
    ///     This property contains the stage this source belongs to; see
    ///     <see cref="ClusterConfigBackfillStages" />.
    /// </summary>
    int BackfillStage { get; }

    /// <summary>
    ///     This method reads everything this node holds of the category. It takes one snapshot of
    ///     the identifiers and then loads each row by identifier, so a row deleted during the
    ///     read is skipped (its tombstone is a separate item) and a row is never skipped because
    ///     another one was deleted.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>One envelope per row that live sync would send, in no particular order.</returns>
    IAsyncEnumerable<ClusterConfigSyncEnvelope> ReadBackfillAsync(CancellationToken stoppingToken);
}
