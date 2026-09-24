// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Channel;

/// <summary>
///     This class is the body <c>ClusterChannelController.UsernameCheck</c> answers with.
/// </summary>
/// <param name="Exists">
///     Whether the responding peer already has an account with the requested normalized username.
/// </param>
public record ClusterUsernameCheckResult(bool Exists);
