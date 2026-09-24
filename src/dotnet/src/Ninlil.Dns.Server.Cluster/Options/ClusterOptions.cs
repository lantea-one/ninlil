// Define our namespace.
namespace Ninlil.Dns.Server.Cluster.Options;

/// <summary>
///     This class binds to the <c>Cluster</c> section of the application's configuration and
///     describes this node's own identity within a Ninlil cluster -- never a peer's; every other
///     node this instance knows about is real data, in <c>ClusterNodeEntity</c>, not
///     configuration (see that class's own doc comment for why). <see cref="NodeId" /> is
///     generated once, automatically, the first time this host ever starts with clustering
///     available (the same "generate and persist so a fresh installation works with zero
///     configuration" convention <c>Program.cs</c> already applies to the JWT signing key), and
///     then never changes again for the lifetime of this installation -- changing it after this
///     node has already joined a cluster would make every peer's own registry entry for it
///     (keyed by <see cref="NodeId" />, see <c>ClusterNodeEntity.RemoteNodeId</c>) stale.
/// </summary>
public class ClusterOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "Cluster";

    /// <summary>
    ///     This property controls whether this node's own cluster channel endpoints
    ///     (<c>ClusterChannelController</c>) accept any request at all, and whether
    ///     <c>ClusterHeartbeatBackgroundService</c> probes any configured peer. Off by default --
    ///     a capability-expanding toggle per this project's own invariant #6: clustering opens a
    ///     new, distinct authenticated surface, and a single-node deployment (the overwhelming
    ///     majority of this project's homelab/home-router deployment target) has no reason to run
    ///     it at all.
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    ///     This property contains this node's own stable, self-asserted identifier -- generated
    ///     once, automatically, at first startup and persisted to
    ///     <c>data/cluster-node-id.txt</c> next to the application (mirroring
    ///     <c>Program.cs</c>'s own <c>jwt-signing-key.txt</c> convention exactly), never
    ///     operator-editable through the REST API. Every peer that joins with this node records
    ///     this value as its own <c>ClusterNodeEntity.RemoteNodeId</c> for this row.
    /// </summary>
    public Guid NodeId { get; set; }

    /// <summary>
    ///     This property contains the hostname or IP address a peer should use to reach this
    ///     node's own cluster channel -- included in every invite this node issues
    ///     (<c>ClusterInviteCodec</c>) and in this node's own join-completion callback payload.
    ///     Left blank by default: unlike an operator-provided PFX path or a self-signed subject
    ///     alternative name, this project has no safe way to guess which of a homelab host's own
    ///     network interfaces/addresses a peer on a different network segment should actually
    ///     dial, so an operator must set this explicitly before generating or accepting an
    ///     invite -- <c>ClusterController</c> rejects both actions with a <see cref="Ninlil.Dns.Common.Exceptions.ValidationException" />
    ///     while this is blank, rather than silently advertising an unusable value.
    /// </summary>
    public string AdvertisedHost { get; set; } = "";

    /// <summary>
    ///     This property contains the TCP port a peer should use to reach this node's own
    ///     cluster channel. Left at <c>0</c> by default, meaning "use this host's own shared
    ///     Kestrel HTTPS port" (<c>WebListenOptions.HttpsPort</c>) -- the cluster channel is a
    ///     set of ordinary controllers mapped onto that same listener (see
    ///     <c>ClusterChannelController</c>'s own doc comment for why), so almost every real
    ///     deployment never needs to set this at all; it exists purely for the unusual case of a
    ///     reverse proxy or NAT rule that maps a different external port onto this host's own
    ///     internal HTTPS port.
    /// </summary>
    public int AdvertisedPort { get; set; }

    /// <summary>
    ///     This property contains how often <c>ClusterHeartbeatBackgroundService</c> probes each
    ///     known peer. Defaults to 30 seconds -- frequent enough that a real outage in this
    ///     project's typical 2-to-5-node homelab cluster is detected within roughly a minute and
    ///     a half (see <see cref="MissedHeartbeatThreshold" />), without generating meaningful
    ///     network or CPU load even over a modest WAN link between two sites.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     This property contains how long a single heartbeat probe against one peer may take
    ///     before it is treated as a failure. Defaults to 10 seconds -- generous enough to
    ///     tolerate a slow WAN link or a momentarily busy peer without a false failure, while
    ///     still comfortably shorter than <see cref="HeartbeatInterval" /> so one slow probe
    ///     never delays the next scheduled round.
    /// </summary>
    public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     This property contains how many consecutive failed heartbeat probes against a peer
    ///     this node tolerates before marking it <c>ClusterNodeStatusEnum.Unreachable</c> in its
    ///     own registry. Defaults to 3 -- enough to absorb a single transient blip (a dropped
    ///     packet, a momentary DNS hiccup resolving the peer's own hostname) without flapping
    ///     status on every minor network hiccup, while still surfacing a genuine outage within
    ///     roughly <see cref="MissedHeartbeatThreshold" /> times <see cref="HeartbeatInterval" />
    ///     (about a minute and a half, at the defaults).
    /// </summary>
    public int MissedHeartbeatThreshold { get; set; } = 3;
}
