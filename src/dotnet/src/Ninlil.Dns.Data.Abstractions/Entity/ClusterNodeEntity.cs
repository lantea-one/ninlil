using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Entity;

/// <summary>
///     This class represents one peer in the local cluster registry: one row per <em>other</em>
///     node this instance knows and probes over the TSIG-authenticated, TLS-encrypted
///     node-to-node channel (see <c>Server.Cluster</c>). There is no row for the local node; its
///     identity, address and certificate are configuration in <c>ClusterOptions</c>.
///     <para>
///         The cluster is an active-active pool of interchangeable members. Each node keeps its
///         own registry of the peers it joined, with no elected coordinator or shared membership
///         list, so two nodes can briefly disagree about a third peer's <see cref="Status" />.
///         That is an accepted simplification at homelab scale.
///     </para>
/// </summary>
public class ClusterNodeEntity : AuditableEntityBase
{
    /// <summary>
    ///     This property contains the operator-chosen name of this peer (for example
    ///     "ninlil-basement" or "ninlil-office"), shown on the console's Cluster page. The heartbeat
    ///     does not use it.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    ///     This property contains the stable node identifier the peer reports: its
    ///     <c>ClusterOptions.NodeId</c>. <see cref="EntityBase.Id" /> only identifies the local row.
    ///     Keeping the peer's own identifier lets the join handshake spot a peer already known under
    ///     another address, and lets a heartbeat response be checked against the identity the peer
    ///     claims rather than only the address it was reached at.
    /// </summary>
    public Guid RemoteNodeId { get; set; }

    /// <summary>
    ///     This property contains the hostname or IP address of the peer's cluster channel.
    /// </summary>
    public string Host { get; set; }

    /// <summary>
    ///     This property contains the TCP port of the peer's cluster channel. The channel shares the
    ///     peer's Kestrel HTTPS port rather than having its own listener, and the port need not match
    ///     the local one.
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    ///     This property contains the name of the <see cref="TsigKeyEntity" /> that signs requests to
    ///     this peer and verifies requests from it. Like <see cref="ZoneEntity.TsigKeyName" />, it is
    ///     a reference by name rather than a foreign key, since a key is not tied to one consumer.
    /// </summary>
    public string TsigKeyName { get; set; }

    /// <summary>
    ///     This property contains the SHA-256 thumbprint (lowercase hex) of the TLS certificate the
    ///     peer presents on its cluster channel, learned during the join handshake and pinned from
    ///     then on. <c>ClusterPeerHttpClientFactory</c> rejects a connection whose leaf certificate
    ///     does not match, instead of relying on chain trust, because most Ninlil installations use
    ///     a self-signed certificate (see <c>TlsCertificateOptions</c>).
    /// </summary>
    public string TlsCertificateThumbprint { get; set; }

    /// <summary>
    ///     This property contains the peer's status as last observed by the local node; see
    ///     <see cref="ClusterNodeStatusEnum" /> for why it is a local view.
    /// </summary>
    public ClusterNodeStatusEnum Status { get; set; } = ClusterNodeStatusEnum.Joining;

    /// <summary>
    ///     This property contains the UTC date and time of the last successful heartbeat probe
    ///     against the peer, or <see langword="null" /> if none has succeeded.
    /// </summary>
    public DateTimeOffset? LastHeartbeatAtUtc { get; set; }

    /// <summary>
    ///     This property contains how many heartbeat probes against this peer have failed in a row.
    ///     When it reaches <c>ClusterOptions.MissedHeartbeatThreshold</c>, <see cref="Status" />
    ///     becomes <see cref="ClusterNodeStatusEnum.Unreachable" />. A successful probe resets it to
    ///     zero.
    /// </summary>
    public int MissedHeartbeatCount { get; set; }

    /// <summary>
    ///     This property contains free-form operator notes about this peer.
    /// </summary>
    public string Comments { get; set; }
}
