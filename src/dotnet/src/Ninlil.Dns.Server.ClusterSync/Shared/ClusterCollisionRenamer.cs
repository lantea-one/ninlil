// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Shared;

/// <summary>
///     This class builds the rename and note for a name collision, shared by
///     <c>UserClusterConfigSyncApplier</c> and <c>RoleClusterConfigSyncApplier</c>. When two
///     nodes create a user or role with the same unique name (for example during a network
///     partition), the loser of the last-write-wins comparison is renamed and given an
///     explanatory note, never dropped or merged (see <c>UserEntity.ClusterConflictNote</c>).
/// </summary>
public static class ClusterCollisionRenamer
{
    /// <summary>
    ///     This method builds the name a collision loser gets: the original value plus
    ///     <c>-conflict-</c> and the other node's id, cut to 256 characters. It is deterministic,
    ///     so nodes that pass the same node id compute the same name.
    /// </summary>
    /// <param name="originalValue">The value that collided (a username or a role name).</param>
    /// <param name="conflictingNodeId">
    ///     The node id of the other side of the collision, the one not renamed.
    /// </param>
    public static string BuildRenamedValue(string originalValue, Guid conflictingNodeId) =>
        $"{originalValue}-conflict-{conflictingNodeId:N}"[
            ..Math.Min($"{originalValue}-conflict-{conflictingNodeId:N}".Length, 256)
        ];

    /// <summary>
    ///     This method builds the explanation stored in the loser's <c>ClusterConflictNote</c>,
    ///     which the REST API and the user and role lists in the web UI show, so the rename is
    ///     visible.
    /// </summary>
    /// <param name="originalValue">The original value (username or role name).</param>
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
