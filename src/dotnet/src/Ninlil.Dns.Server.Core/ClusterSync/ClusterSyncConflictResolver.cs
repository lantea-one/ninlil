// Define our namespace.
namespace Ninlil.Dns.Server.Core.ClusterSync;

/// <summary>
///     This class implements the cluster's own last-write-wins conflict-resolution rule, once,
///     shared by every <see cref="IClusterConfigSyncApplier" /> implementation -- a deliberate,
///     documented simplification for this project's homelab-scale cluster target, not a
///     consensus system: there is no vector clock,
///     no quorum, and no cluster-wide agreement on which change "really" happened first when two
///     nodes edit the same entity within the same clock tick on two different, unsynchronized
///     system clocks.
/// </summary>
public static class ClusterSyncConflictResolver
{
    /// <summary>
    ///     This method decides whether an incoming change should overwrite what a node already
    ///     has locally for the same entity.
    ///     <para>
    ///         The primary rule is a straightforward timestamp comparison: whichever of the two
    ///         <see cref="DateTimeOffset" /> values is strictly later wins outright.
    ///     </para>
    ///     <para>
    ///         On a genuine exact tie (the same instant, to the precision both nodes' clocks
    ///         happened to record it at) -- the one case an ordinary timestamp comparison cannot
    ///         resolve -- this method falls back to comparing the two originating node
    ///         identifiers' own canonical <c>"D"</c>-format string form, ordinally, and the
    ///         lexicographically greater one wins. This tie-break is arbitrary (there is no
    ///         meaningful reason node id <c>"b2..."</c> should "outrank" <c>"a1..."</c>), but it
    ///         is deterministic: every node in the cluster reaches the exact same conclusion about
    ///         which of two simultaneous writes wins, regardless of which one it happens to
    ///         receive first -- the one property that actually matters for keeping every node's
    ///         own copy converged.
    ///     </para>
    /// </summary>
    /// <param name="localLastModifiedUtc">
    ///     The timestamp this node already has recorded locally for this entity -- for an entity
    ///     this node has never seen before, pass <see cref="DateTimeOffset.MinValue" /> so the
    ///     incoming change always wins.
    /// </param>
    /// <param name="localOriginatingNodeId">
    ///     The node id already recorded locally, or <see langword="null" /> for an entity that
    ///     predates this project's cluster config sync (a purely local write from before
    ///     clustering existed at all, or a still-single-node deployment) -- treated as
    ///     <see cref="Guid.Empty" /> for the tie-break comparison, so an incoming change from any
    ///     real, non-empty node id always wins a tie against an entity with no recorded origin.
    /// </param>
    /// <param name="incomingLastModifiedUtc">The timestamp the incoming change carries.</param>
    /// <param name="incomingOriginatingNodeId">The node id the incoming change carries.</param>
    /// <returns>
    ///     <see langword="true" /> if the incoming change should be applied (it is newer, or wins
    ///     the tie-break); <see langword="false" /> if the local state should be kept unchanged.
    /// </returns>
    public static bool IncomingWins(
        DateTimeOffset localLastModifiedUtc,
        Guid? localOriginatingNodeId,
        DateTimeOffset incomingLastModifiedUtc,
        Guid incomingOriginatingNodeId
    )
    {
        if (incomingLastModifiedUtc != localLastModifiedUtc)
            return incomingLastModifiedUtc > localLastModifiedUtc;

        string localNodeIdText = (localOriginatingNodeId ?? Guid.Empty).ToString("D");
        string incomingNodeIdText = incomingOriginatingNodeId.ToString("D");

        return string.CompareOrdinal(incomingNodeIdText, localNodeIdText) > 0;
    }
}
