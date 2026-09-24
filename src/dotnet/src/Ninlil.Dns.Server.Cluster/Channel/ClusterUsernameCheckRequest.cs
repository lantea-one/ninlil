// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Channel;

/// <summary>
///     This class is the body <c>ClusterUsernameUniquenessChecker</c> sends to
///     <c>ClusterChannelController.UsernameCheck</c> on every currently healthy peer, as part of
///     Phase 9-6d's own best-effort, cluster-wide username-uniqueness check performed at
///     user-creation time -- see <c>IClusterUsernameUniquenessChecker</c>'s own doc comment for
///     the full design.
/// </summary>
/// <param name="NormalizedUserName">The upper-invariant candidate username being checked.</param>
public record ClusterUsernameCheckRequest(string NormalizedUserName);
