// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Shared;

/// <summary>
///     This class implements the one real, shared collision-resolution shape both
///     <c>UserClusterConfigSyncApplier</c> and <c>RoleClusterConfigSyncApplier</c> need: when two
///     nodes independently create an entity with the same unique name during a real network
///     partition, the deterministic last-write-wins loser must be renamed to something
///     unambiguous and flagged with a human-readable explanation, never silently dropped or
///     silently merged into the winner -- see <c>UserEntity.ClusterConflictNote</c>'s own doc
///     comment for the full design.
/// </summary>
public static class ClusterCollisionRenamer
{
    /// <summary>
    ///     This method builds the renamed value a collision loser is given -- the original value,
    ///     with a short, deterministic, human-legible suffix identifying the node whose write
    ///     caused the rename. Deterministic (not random) so that every node in the cluster,
    ///     independently reaching the same collision-resolution decision, computes the exact same
    ///     renamed value.
    /// </summary>
    /// <param name="originalValue">The value that collided (a username or a role name).</param>
    /// <param name="conflictingNodeId">
    ///     The node id of the <em>other</em> side of the collision -- the side that is not being
    ///     renamed.
    /// </param>
    public static string BuildRenamedValue(string originalValue, Guid conflictingNodeId) =>
        $"{originalValue}-conflict-{conflictingNodeId:N}"[
            ..Math.Min($"{originalValue}-conflict-{conflictingNodeId:N}".Length, 256)
        ];

    /// <summary>
    ///     This method builds the human-readable explanation stored on the collision loser's own
    ///     <c>ClusterConflictNote</c> field -- surfaced to an administrator via the REST API and
    ///     the Angular admin SPA's own user/role list, so the rename is genuinely visible.
    /// </summary>
    /// <param name="originalValue">The original, now-reassigned value (username or role name).</param>
    /// <param name="conflictingNodeId">The node id of the winning side of the collision.</param>
    /// <param name="detectedAtUtc">The UTC instant this collision was detected and resolved.</param>
    public static string BuildConflictNote(
        string originalValue,
        Guid conflictingNodeId,
        DateTimeOffset detectedAtUtc
    ) =>
        $"Renamed from '{originalValue}' on {detectedAtUtc:u} due to a cluster-wide uniqueness "
        + $"conflict: another node ({conflictingNodeId:D}) independently created an account with "
        + "the same name during a network partition, and this account lost the deterministic "
        + "tie-break. The original account is preserved under this renamed identity -- nothing "
        + "was dropped or merged.";
}
