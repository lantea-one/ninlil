// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Channel;

/// <summary>
///     This record is the body <c>ClusterChannelController.UsernameCheck</c> answers with, read
///     by <see cref="ClusterUsernameUniquenessChecker" />.
/// </summary>
/// <param name="Exists">
///     Whether the responding peer already has an account with the requested normalized username.
/// </param>
public record ClusterUsernameCheckResult(bool Exists);
