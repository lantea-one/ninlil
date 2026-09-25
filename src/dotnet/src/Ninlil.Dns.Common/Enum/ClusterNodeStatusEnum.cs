using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Serializer.Attribute;

// Define our namespace.
namespace Ninlil.Dns.Common.Enum;

/// <summary>
///     This enumeration lists the states a <c>ClusterNodeEntity</c> row can be in, as seen by the
///     local node. It is Ninlil vocabulary for cluster membership, not an external standard.
///     <para>
///         The status reflects only what <em>this</em> node has seen when probing the peer over
///         the node-to-node channel (<c>ClusterHeartbeatBackgroundService</c> in
///         <c>Server.Cluster</c>). It is not agreed across the cluster, so two nodes can disagree
///         about a third. That is an accepted simplification at homelab scale.
///     </para>
///     <para>
///         There is no "removed" state. Removing a peer deletes its row
///         (<c>ClusterController.RemoveNode</c>), like other hard deletes such as
///         <c>TsigKeysController.Delete</c>.
///     </para>
/// </summary>
[JsonConverter<EnumDescriptionJsonConverter<ClusterNodeStatusEnum>>]
public enum ClusterNodeStatusEnum
{
    /// <summary>
    ///     The node was just added to the local peer registry by either half of the join handshake
    ///     (<c>ClusterController.Join</c> or <c>ClusterChannelController.JoinComplete</c>), and no
    ///     heartbeat probe against it has succeeded yet.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Joining")]
    Joining = 0,

    /// <summary>
    ///     The local node's most recent heartbeat probe against the peer succeeded.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Healthy")]
    Healthy = 1,

    /// <summary>
    ///     The local node's heartbeat probes against the peer have failed
    ///     <c>ClusterOptions.MissedHeartbeatThreshold</c> times in a row.
    /// </summary>
    [System.Runtime.Serialization.EnumMember(Value = "Unreachable")]
    Unreachable = 2,
}
