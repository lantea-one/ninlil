// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Channel;

/// <summary>
///     This record is the body <c>ClusterHeartbeatBackgroundService</c> sends with every probe.
///     <c>ClusterChannelController.Heartbeat</c> answers any request with a valid signature
///     (each node tracks only its own view of its peers); <see cref="SenderNodeId" /> only lets
///     the peer log who probed, which helps when a key and node id do not match.
/// </summary>
/// <param name="SenderNodeId">The probing node's <c>ClusterOptions.NodeId</c>.</param>
/// <param name="SentAtUtc">The UTC moment the probe was sent.</param>
public record ClusterHeartbeatRequest(Guid SenderNodeId, DateTimeOffset SentAtUtc);
