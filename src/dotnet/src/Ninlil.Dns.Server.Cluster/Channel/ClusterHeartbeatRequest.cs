// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Channel;

/// <summary>
///     This class is the body <c>ClusterHeartbeatBackgroundService</c> sends on every heartbeat
///     probe. The receiving <c>ClusterChannelController.Heartbeat</c> action needs nothing from
///     this body to decide whether to answer -- a valid TSIG signature over the request is the
///     entire trust decision (see that action's own doc comment for why the sending node's own
///     view of a peer's health, not the receiving node's, is this project's chosen source of
///     truth) -- but <see cref="SenderNodeId" /> lets the receiving node log which peer it heard
///     from, useful for diagnosing a mismatched key/node-id pairing.
/// </summary>
/// <param name="SenderNodeId">The probing node's own stable <c>ClusterOptions.NodeId</c>.</param>
/// <param name="SentAtUtc">The UTC moment the probe was sent.</param>
public record ClusterHeartbeatRequest(Guid SenderNodeId, DateTimeOffset SentAtUtc);
