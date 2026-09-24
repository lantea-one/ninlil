using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration lists the states a <c>ClusterNodeEntity</c> row can be in from the local
///     node's own point of view -- Ninlil's own operational vocabulary for cluster membership,
///     not an externally standardized value. A node's own status here reflects only what
///     <em>this</em> node has observed by pinging the peer over the node-to-node channel (see
///     <c>Server.Cluster</c>'s <c>ClusterHeartbeatBackgroundService</c>); it is never a
///     cluster-wide, agreed-upon fact -- two nodes can disagree about a third peer's status at
///     any given moment, which is an accepted, documented simplification for this project's
///     homelab-scale deployment target, the same kind of tradeoff already accepted for the
///     DNS-query rate limiter's own fail-open behavior. A row is never left in a fourth
///     "removed" state: an operator removing a peer node deletes its row outright (see
///     <c>ClusterController.RemoveNode</c>), the same hard-delete convention
///     <c>TsigKeysController.Delete</c>/<c>WebhookSubscriptionsController.Delete</c> already
///     establish, rather than this enumeration carrying a value that would otherwise sit
///     permanently unused between an operator's removal action and the row's own deletion.
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<ClusterNodeStatusEnum>>]
public enum ClusterNodeStatusEnum
{
    /// <summary>
    ///     The node was just added to this node's own peer registry (through either half of the
    ///     join handshake -- see <c>ClusterController.Join</c> and
    ///     <c>ClusterChannelController.JoinComplete</c>) but this node has not yet completed a
    ///     single successful heartbeat probe against it.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Joining")]
    Joining = 0,

    /// <summary>
    ///     This node's own most recent heartbeat probe against the peer succeeded.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Healthy")]
    Healthy = 1,

    /// <summary>
    ///     This node's own heartbeat probes against the peer have failed
    ///     <c>ClusterOptions.MissedHeartbeatThreshold</c> times in a row.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Unreachable")]
    Unreachable = 2,
}
