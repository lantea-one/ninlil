// Define our namespace.
namespace Ninlil.Dns.Server.Core.ClusterSync;

/// <summary>
///     This record is the wire shape for one operational config change sent between cluster
///     nodes: an entity type, a key, the new content (unless it was a deletion), and the
///     last-write-wins state (a UTC timestamp and the originating node's ID).
///     <para>
///         Every synced category (filter lists, filter list entries, access-control rules,
///         rewrites, forwarders, users, roles, settings sections) uses this one shape, so
///         <c>ClusterChannelController</c>'s inbound endpoint and the broadcaster are written
///         once.
///     </para>
///     <para>
///         <see cref="PayloadJson" /> is a pre-serialized string because <c>Server.Core</c> does
///         not reference <c>Ninlil.Dns.Data.Abstractions</c> and cannot know the entity types.
///         Only the matching <see cref="IClusterConfigSyncApplier" /> deserializes it.
///     </para>
/// </summary>
/// <param name="EntityType">
///     The kind of thing carried, matching an <see cref="IClusterConfigSyncApplier.EntityType" />
///     (for example <c>"FilterList"</c>, <c>"AccessControlRule"</c> or <c>"Settings"</c>).
/// </param>
/// <param name="EntityKey">
///     The entity's key within <see cref="EntityType" />: a <see cref="Guid" /> string for row
///     entities, or a settings section name (for example <c>"EventBroker"</c>) for
///     <c>"Settings"</c>.
/// </param>
/// <param name="IsDeleted">
///     Whether this change is a deletion. When <see langword="true" />,
///     <see cref="PayloadJson" /> is <see langword="null" />.
/// </param>
/// <param name="PayloadJson">
///     The new content as JSON in the shape the matching <see cref="IClusterConfigSyncApplier" />
///     expects, or <see langword="null" /> for a deletion.
/// </param>
/// <param name="LastModifiedUtc">
///     When the change happened on the originating node, from that node's clock. Last-write-wins
///     compares this (<see cref="ClusterSyncConflictResolver.IncomingWins" />); node clocks are not
///     assumed to be synchronized.
/// </param>
/// <param name="OriginatingNodeId">
///     The node (<c>ClusterOptions.NodeId</c>) the change happened on, kept unchanged as the
///     change is re-synced, and used to break a tie on <see cref="LastModifiedUtc" />.
/// </param>
public sealed record ClusterConfigSyncEnvelope(
    string EntityType,
    string EntityKey,
    bool IsDeleted,
    string PayloadJson,
    DateTimeOffset LastModifiedUtc,
    Guid OriginatingNodeId
);
