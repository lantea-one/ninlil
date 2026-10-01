// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Channel;

/// <summary>
///     This record is the body of the joining node's callback to its inviter, the second half of
///     the join handshake. <see cref="Join.ClusterJoinService.JoinAsync" /> sends it and
///     <c>ClusterChannelController.JoinComplete</c> receives it. The inviter records the joiner
///     as a peer on the strength of the request's signature alone (see
///     <see cref="Join.ClusterJoinService.HandleJoinCompleteAsync" />).
/// </summary>
/// <param name="JoinerNodeId">The joining node's <c>ClusterOptions.NodeId</c>.</param>
/// <param name="JoinerHost">The host name or IP address at which the inviter reaches the joiner's cluster channel.</param>
/// <param name="JoinerPort">The TCP port at which the inviter reaches the joiner's cluster channel.</param>
/// <param name="JoinerCertificateThumbprint">The SHA-256 thumbprint of the joiner's TLS certificate, pinned for later requests.</param>
/// <param name="JoinerDisplayName">
///     An optional display name for the joiner, shown on the inviter's Cluster page; without
///     one, a name derived from <paramref name="JoinerNodeId" /> is used.
/// </param>
public record ClusterJoinCompleteRequest(
    Guid JoinerNodeId,
    string JoinerHost,
    int JoinerPort,
    string JoinerCertificateThumbprint,
    string JoinerDisplayName
);
