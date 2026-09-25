// Define our namespace.
namespace Ninlil.Dns.Server.Core.ClusterSync;

/// <summary>
///     This class holds the last-write-wins rule that every <see cref="IClusterConfigSyncApplier" />
///     uses. It is a simple rule for small clusters, not consensus: there is no vector clock or
///     quorum, and node clocks are not synchronized, so with clock skew an older edit can win.
/// </summary>
public static class ClusterSyncConflictResolver
{
    /// <summary>
    ///     This method decides whether an incoming change should overwrite the local copy.
    ///     <para>
    ///         The later timestamp wins. On an exact tie, the node IDs are compared as
    ///         <c>"D"</c>-format strings, ordinally, and the greater one wins. That choice is
    ///         arbitrary but deterministic, so every node picks the same winner whichever write
    ///         it receives first, and all copies converge.
    ///     </para>
    /// </summary>
    /// <param name="localLastModifiedUtc">
    ///     The timestamp recorded locally for this entity. For an entity this node has never
    ///     seen, pass <see cref="DateTimeOffset.MinValue" /> so the incoming change wins.
    /// </param>
    /// <param name="localOriginatingNodeId">
    ///     The node ID recorded locally, or <see langword="null" /> for an entity with no recorded
    ///     origin (written before clustering was set up). That is treated as
    ///     <see cref="Guid.Empty" />, so any real node ID wins a tie against it.
    /// </param>
    /// <param name="incomingLastModifiedUtc">The timestamp the incoming change carries.</param>
    /// <param name="incomingOriginatingNodeId">The node id the incoming change carries.</param>
    /// <returns>
    ///     <see langword="true" /> if the incoming change should be applied (newer, or wins the
    ///     tie); <see langword="false" /> to keep the local copy.
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
