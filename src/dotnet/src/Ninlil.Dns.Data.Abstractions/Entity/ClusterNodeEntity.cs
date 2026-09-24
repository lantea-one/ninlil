using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents a single peer node in this server's own cluster membership
///     registry -- one row per <em>other</em> node this instance knows about and actively
///     probes over its dedicated, TSIG-authenticated + TLS-encrypted node-to-node channel (see
///     <c>Server.Cluster</c>). There is deliberately no row representing this node itself; its
///     own identity/address/certificate live in <c>ClusterOptions</c> instead (see that class's
///     own doc comment), the same "self is configuration, peers are data" split
///     <c>WebListenOptions</c> (this node's own listen address) versus, say,
///     <c>DnsForwarderEntity</c> (an external server this node talks to) already draws.
///     <para>
///         Ninlil's cluster model is a genuine active-active pool: every member is fully
///         interchangeable, and each node maintains its own, independent registry of every peer
///         it has joined with -- there is no single elected coordinator or shared, agreed-upon
///         membership list. Two nodes can therefore each hold a slightly different view of a
///         third peer's own <see cref="Status" /> at any given moment; this is an accepted
///         simplification for this project's homelab-scale deployment target, not a bug.
///     </para>
/// </summary>
public class ClusterNodeEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains an operator-chosen, human-readable name for this peer (for
    ///     example, "ninlil-basement" or "ninlil-office") -- shown throughout the Angular
    ///     "Cluster" page, never consulted by the heartbeat engine itself.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     This property contains the peer's own self-asserted, stable node identifier -- the
    ///     value its own <c>ClusterOptions.NodeId</c> holds on its side. Recording it here (in
    ///     addition to this row's own <see cref="EntityBase.Id" />, which only identifies this
    ///     registry row locally) lets the join handshake detect a peer that is already known
    ///     under a different address, and lets a heartbeat response be cross-checked against the
    ///     identity the peer claims for itself, not merely the address it happened to be reached
    ///     at.
    /// </summary>
    public Guid RemoteNodeId { get; set; }

    /// <summary>
    ///     This property contains the hostname or IP address this node reaches the peer's own
    ///     cluster channel at.
    /// </summary>
    public string Host { get; set; }

    /// <summary>
    ///     This property contains the TCP port this node reaches the peer's own cluster channel
    ///     at -- the peer's own shared Kestrel HTTPS port (see <c>Program.cs</c>'s own doc
    ///     comment on why the cluster channel is not a separate listener), not necessarily the
    ///     same port number this node itself listens on.
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    ///     This property contains the name of the <see cref="TsigKeyEntity" /> this node signs
    ///     its own outbound requests to this peer with, and verifies the peer's own inbound
    ///     requests against -- the same soft-reference-by-name convention
    ///     <see cref="ZoneEntity.TsigKeyName" /> already establishes, rather than a real foreign
    ///     key, since a key is not otherwise scoped to any one consumer.
    /// </summary>
    public string TsigKeyName { get; set; }

    /// <summary>
    ///     This property contains the SHA-256 thumbprint (lowercase hex) of the TLS certificate
    ///     the peer is expected to present on its own cluster channel, learned once during the
    ///     join handshake and pinned from then on -- <c>ClusterPeerHttpClientFactory</c> rejects
    ///     any connection to this peer whose presented leaf certificate does not match this exact
    ///     value, rather than relying on ordinary certificate-chain trust, since most Ninlil
    ///     deployments present a self-signed certificate with no real chain to validate in the
    ///     first place (see <c>TlsCertificateOptions</c>).
    /// </summary>
    public string TlsCertificateThumbprint { get; set; }

    /// <summary>
    ///     This property contains this node's own most recently observed status for the peer --
    ///     see <see cref="ClusterNodeStatusEnum" />'s own doc comment for why this is a
    ///     local-only observation, not a cluster-wide fact.
    /// </summary>
    public ClusterNodeStatusEnum Status { get; set; } = ClusterNodeStatusEnum.Joining;

    /// <summary>
    ///     This property contains the UTC date and time this node's own heartbeat probe against
    ///     the peer last succeeded, or <see langword="null" /> if it never has.
    /// </summary>
    public DateTimeOffset? LastHeartbeatAtUtc { get; set; }

    /// <summary>
    ///     This property contains how many consecutive heartbeat probes against this peer have
    ///     failed since the last success -- compared against
    ///     <c>ClusterOptions.MissedHeartbeatThreshold</c> to decide when <see cref="Status" />
    ///     moves to <see cref="ClusterNodeStatusEnum.Unreachable" />, and reset to zero the
    ///     moment a probe succeeds again.
    /// </summary>
    public int MissedHeartbeatCount { get; set; }

    /// <summary>
    ///     This property contains free-form operator notes about this peer.
    /// </summary>
    public string Comments { get; set; }
}
