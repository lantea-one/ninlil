// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Channel;

/// <summary>
///     This record is the body <c>ClusterHeartbeatBackgroundService</c> sends with every probe.
///     <c>ClusterChannelController.Heartbeat</c> passes it to <see cref="Heartbeat.ClusterHeartbeatReceiver" />,
///     which checks that <see cref="SenderNodeId" /> is the peer the signing key belongs to and
///     re-pins that peer's certificate when <see cref="SenderCertificateThumbprint" /> has
///     changed. Each node still sets a peer's status only from its own probes.
/// </summary>
/// <param name="SenderNodeId">The probing node's <c>ClusterOptions.NodeId</c>.</param>
/// <param name="SentAtUtc">The UTC moment the probe was sent.</param>
/// <param name="SenderCertificateThumbprint">
///     The SHA-256 thumbprint of the TLS certificate the probing node presents now (see
///     <see cref="Http.ClusterPeerCertificateValidator.ComputeThumbprint" />), so a renewed
///     certificate is learned over the authenticated channel. Older nodes do not send it.
/// </param>
public record ClusterHeartbeatRequest(
    Guid SenderNodeId,
    DateTimeOffset SentAtUtc,
    string SenderCertificateThumbprint = null
);
