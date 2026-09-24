// Define our namespace.
namespace Ninlil.Dns.Server.Core.ClusterSync;

/// <summary>
///     This interface is the cluster's own best-effort, cluster-wide username-uniqueness check,
///     performed at user-creation time only. <c>Ninlil.Dns.Server.Cluster</c>'s own
///     <c>ClusterUsernameUniquenessChecker</c> is the one real implementation, reusing the cluster's own
///     peer registry and TSIG-authenticated + TLS-encrypted channel exactly the way
///     <see cref="IClusterConfigSyncBroadcaster" />'s own real implementation already does --
///     declaring the abstraction here, in <c>Ninlil.Dns.Server.Core</c>, lets
///     <c>Ninlil.Web</c>'s own <c>UsersController</c> depend on it without needing to know
///     anything about the cluster's own peer registry or signing mechanics.
///     <para>
///         By deliberate design, this check is
///         best-effort against <em>currently-reachable</em> peers only -- creating a
///         user is never blocked merely because one peer happens to be unreachable at that moment.
///         A genuine collision that only becomes visible once a network partition heals (two nodes
///         independently creating the same username while unable to see each other) is instead
///         caught and resolved later, once the nodes resync, by the same last-write-wins rule
///         <see cref="ClusterSyncConflictResolver" /> already implements -- see
///         <c>UserClusterConfigSyncApplier</c>'s own doc comment for that half of the mechanism.
///     </para>
/// </summary>
public interface IClusterUsernameUniquenessChecker
{
    /// <summary>
    ///     This method asks every currently <c>ClusterNodeStatusEnum.Healthy</c> peer whether it
    ///     already has an account with the given normalized username. A peer that does not respond
    ///     within a short timeout, or that is not currently healthy, is simply skipped -- never
    ///     treated as "the name is taken," and never treated as a reason to fail this check
    ///     outright. A no-op, cheaply returning <see langword="false" />, when clustering itself is
    ///     disabled or no peer is currently known.
    /// </summary>
    /// <param name="normalizedUserName">The upper-invariant candidate username to check.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     <see langword="true" /> if at least one currently-reachable, healthy peer already has an
    ///     account with this normalized username; otherwise <see langword="false" />.
    /// </returns>
    Task<bool> ExistsOnAnyHealthyPeerAsync(
        string normalizedUserName,
        CancellationToken stoppingToken = default
    );
}
