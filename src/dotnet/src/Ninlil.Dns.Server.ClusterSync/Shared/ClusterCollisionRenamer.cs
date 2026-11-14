// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Shared;

/// <summary>
///     This class builds the rename and note for a name collision, shared by
///     <c>UserClusterConfigSyncApplier</c> and <c>RoleClusterConfigSyncApplier</c>. When two
///     nodes create a user or role with the same unique name (for example during a network
///     partition), the loser of the last-write-wins comparison is renamed and given an
///     explanatory note, never dropped or merged (see <c>UserEntity.ClusterConflictNote</c>). The
///     loser is named after the node that created it.
/// </summary>
public static class ClusterCollisionRenamer
{
    /// <summary>
    ///     This constant contains the longest user or role name the stores accept.
    /// </summary>
    private const int MaxNameLength = 256;

    /// <summary>
    ///     This method builds the name a collision loser gets: the original value plus
    ///     <c>-conflict-</c> and the id of the node that created the loser, as 32 hex digits. The
    ///     name is shortened, never the suffix, so it fits 256 characters and still names the node.
    ///     It is deterministic, and two losers with the same name come from different nodes, so
    ///     every node computes the same name for a loser and the names of two losers differ.
    /// </summary>
    /// <param name="originalValue">The value that collided (a username or a role name).</param>
    /// <param name="loserNodeId">
    ///     The id of the node that created the renamed account or role. For a row with no
    ///     recorded origin (the bootstrap administrator, or any row written before clustering was
    ///     switched on) the caller passes the id of the node that holds it, which is the node that
    ///     wrote it.
    /// </param>
    /// <exception cref="ArgumentException">
    ///     <paramref name="loserNodeId" /> is <see cref="Guid.Empty" />, which names no node.
    /// </exception>
    public static string BuildRenamedValue(string originalValue, Guid loserNodeId)
    {
        if (loserNodeId == Guid.Empty)
        {
            throw new ArgumentException(
                "A renamed value is named after a real node; an all-zero node id names none.",
                nameof(loserNodeId)
            );
        }

        string suffix = $"-conflict-{loserNodeId:N}";
        int keptLength = Math.Min(originalValue.Length, MaxNameLength - suffix.Length);
        return originalValue[..keptLength] + suffix;
    }

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
