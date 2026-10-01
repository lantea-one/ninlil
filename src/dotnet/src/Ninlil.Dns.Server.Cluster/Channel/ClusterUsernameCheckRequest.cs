// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Channel;

/// <summary>
///     This record is the body <see cref="ClusterUsernameUniquenessChecker" /> sends to
///     <c>ClusterChannelController.UsernameCheck</c> on every healthy peer when a user is
///     created, for the best-effort cluster-wide username check (see
///     <c>IClusterUsernameUniquenessChecker</c>).
/// </summary>
/// <param name="NormalizedUserName">The upper-invariant candidate username being checked.</param>
public record ClusterUsernameCheckRequest(string NormalizedUserName);
