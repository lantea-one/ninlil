// Define our namespace.
namespace Ninlil.Dns.Server.Core.ClusterSync;

/// <summary>
///     This interface checks, when a user is created, whether the username already exists on
///     another cluster node. <c>UsersController</c> in <c>Ninlil.Web</c> calls it; the
///     implementation is <c>ClusterUsernameUniquenessChecker</c> in
///     <c>Ninlil.Dns.Server.Cluster</c>, which uses the peer registry and the TSIG-signed, TLS
///     cluster channel.
///     <para>
///         The check is best effort and only covers peers reachable at that moment, so user
///         creation is never blocked by an unreachable peer. Two nodes that create the same
///         username while partitioned are reconciled later by last-write-wins
///         (<see cref="ClusterSyncConflictResolver" />; see <c>UserClusterConfigSyncApplier</c>).
///     </para>
/// </summary>
public interface IClusterUsernameUniquenessChecker
{
    /// <summary>
    ///     This method asks every <c>ClusterNodeStatusEnum.Healthy</c> peer whether it has an
    ///     account with the given normalized username. A peer that is not healthy or does not
    ///     answer within a short timeout is skipped, never treated as "taken" or as a failure. It
    ///     returns <see langword="false" /> at once when clustering is off or there are no peers.
    /// </summary>
    /// <param name="normalizedUserName">The upper-invariant candidate username to check.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     <see langword="true" /> if at least one reachable, healthy peer has an account with this
    ///     normalized username; otherwise <see langword="false" />.
    /// </returns>
    Task<bool> ExistsOnAnyHealthyPeerAsync(
        string normalizedUserName,
        CancellationToken stoppingToken = default
    );
}
