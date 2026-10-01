// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Options;

/// <summary>
///     This class binds to the <c>Cluster</c> section of the application's configuration and
///     describes this node within a cluster. Peers are data in <c>ClusterNodeEntity</c>, not
///     configuration. <c>ClusterSettingsController</c> edits these settings at runtime.
///     <see cref="NodeId" /> is generated at first startup and must not change afterwards:
///     peers key their record of this node by it (<c>ClusterNodeEntity.RemoteNodeId</c>).
/// </summary>
public class ClusterOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "Cluster";

    /// <summary>
    ///     This property controls whether the cluster channel (<c>ClusterChannelController</c>)
    ///     accepts requests, whether invites can be created or used, and whether the heartbeat,
    ///     config sync, username check and statistics collection contact peers. It is off by
    ///     default: clustering opens a new authenticated surface that a single server does not
    ///     need.
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    ///     This property contains this node's identifier, generated at first startup by
    ///     <c>Program.cs</c> and kept in <c>cluster-node-id.txt</c> in the data directory, like
    ///     <c>jwt-signing-key.txt</c>. The REST API cannot change it. Peers store it as
    ///     <c>ClusterNodeEntity.RemoteNodeId</c>.
    /// </summary>
    public Guid NodeId { get; set; }

    /// <summary>
    ///     This property contains the host name or IP address peers use to reach this node's
    ///     cluster channel. It goes into every invite this node issues and into its join callback.
    ///     It is empty by default because there is no safe way to guess which address a peer on
    ///     another network should use, and while it is empty, creating or using an invite fails
    ///     with a <see cref="Ninlil.Dns.Common.Exceptions.ValidationException" />.
    /// </summary>
    public string AdvertisedHost { get; set; } = "";

    /// <summary>
    ///     This property contains the TCP port peers use to reach this node's cluster channel,
    ///     normally the web server's HTTPS port (<c>WebListenOptions.HttpsPort</c>), since the
    ///     channel is a set of controllers on that listener, or another port where a reverse proxy
    ///     or NAT forwards to it. The default is <c>0</c>, and nothing replaces 0 with the HTTPS
    ///     port: it is copied into invites and join callbacks as it is, and an invite with port 0
    ///     is rejected when decoded, so this must be set.
    /// </summary>
    public int AdvertisedPort { get; set; }

    /// <summary>
    ///     This property contains how often <c>ClusterHeartbeatBackgroundService</c> probes each
    ///     known peer. The default of 30 seconds detects an outage in about a minute and a half
    ///     (see <see cref="MissedHeartbeatThreshold" />) with negligible load, even over a WAN
    ///     link. It is read when the heartbeat service starts, so a change needs a restart.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     This property contains how long a single heartbeat probe against one peer may take
    ///     before it is abandoned. The default of 10 seconds allows for a slow WAN link or a busy
    ///     peer and is shorter than <see cref="HeartbeatInterval" />, so a round finishes before
    ///     the next. The join callback uses it too.
    /// </summary>
    public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     This property contains how many consecutive failed heartbeat probes against a peer
    ///     this node allows before marking it <c>ClusterNodeStatusEnum.Unreachable</c>. The
    ///     default of 3 absorbs a single dropped packet or DNS hiccup without flapping, and
    ///     reports an outage after about this many times <see cref="HeartbeatInterval" /> (a
    ///     minute and a half at the defaults).
    /// </summary>
    public int MissedHeartbeatThreshold { get; set; } = 3;
}
