// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Channel;

/// <summary>
///     This class is the body a joining node's own callback to its inviter carries, completing
///     the second half of the join handshake -- see <c>ClusterJoinService.JoinAsync</c> (the
///     sender) and <c>ClusterChannelController.JoinComplete</c> (the receiver) for the full
///     handshake. Successfully authenticating this request (via the shared TSIG key the invite
///     itself carried) is, by itself, proof the joining node possesses that key, so the inviter
///     needs nothing further to record the joiner as a trusted peer.
/// </summary>
/// <param name="JoinerNodeId">The joining node's own stable <c>ClusterOptions.NodeId</c>.</param>
/// <param name="JoinerHost">The hostname/IP address the inviter should reach the joiner's own cluster channel at.</param>
/// <param name="JoinerPort">The TCP port the inviter should reach the joiner's own cluster channel at.</param>
/// <param name="JoinerCertificateThumbprint">The SHA-256 thumbprint of the TLS certificate the joiner presents on its own cluster channel, to pin for future heartbeat probes.</param>
/// <param name="JoinerDisplayName">
///     An optional, operator-chosen display name for the joiner, shown in the inviter's own
///     Angular "Cluster" page for this row -- falls back to a name derived from
///     <paramref name="JoinerNodeId" /> when omitted.
/// </param>
public record ClusterJoinCompleteRequest(
    Guid JoinerNodeId,
    string JoinerHost,
    int JoinerPort,
    string JoinerCertificateThumbprint,
    string JoinerDisplayName
);
