using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Server.Core.ClusterSync;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Shared;

/// <summary>
///     This class holds the two ways an envelope is stamped, so the live sending subscribers, the
///     entity sources and the tombstone source all produce the same last-write-wins state. A
///     receiver compares it with <see cref="ClusterSyncConflictResolver.IncomingWins" />, so a
///     different rule on one path would make a node win or lose against itself.
/// </summary>
public static class ClusterConfigSyncEnvelopes
{
    /// <summary>
    ///     This method builds the envelope of a row that exists.
    /// </summary>
    /// <param name="entityType">The synced entity type (the applier's type).</param>
    /// <param name="entity">The row; its key is its id.</param>
    /// <param name="payloadJson">The payload the matching applier expects.</param>
    /// <param name="ownNodeId">This node's id.</param>
    /// <returns>
    ///     An envelope stamped with the time the local write recorded
    ///     (<see cref="AuditableEntityBaseExtensions.GetLastModifiedUtc" />) and with the node
    ///     that wrote the row. A local write never sets
    ///     <see cref="AuditableEntityBase.OriginatingNodeId" />, only an applier does, so a row
    ///     without one was written here and is attributed to this node. A row an applier wrote
    ///     keeps its origin as it is re-synced, which lets a tie be settled the same way on every
    ///     node.
    /// </returns>
    public static ClusterConfigSyncEnvelope ForEntity(
        string entityType,
        AuditableEntityBase entity,
        string payloadJson,
        Guid ownNodeId
    ) =>
        new(
            entityType,
            entity.Id.ToString(),
            IsDeleted: false,
            payloadJson,
            entity.GetLastModifiedUtc(),
            entity.OriginatingNodeId ?? ownNodeId
        );

    /// <summary>
    ///     This method builds the envelope of a deletion. A deletion has no row left to read, so
    ///     the caller supplies the time and the node.
    /// </summary>
    /// <param name="entityType">The synced entity type of the deleted entity.</param>
    /// <param name="entityKey">The deleted entity's key.</param>
    /// <param name="deletedAtUtc">When the deletion happened.</param>
    /// <param name="originatingNodeId">The node the deletion happened on.</param>
    /// <returns>A bare envelope with no payload.</returns>
    public static ClusterConfigSyncEnvelope ForDeletion(
        string entityType,
        string entityKey,
        DateTimeOffset deletedAtUtc,
        Guid originatingNodeId
    ) =>
        new(
            entityType,
            entityKey,
            IsDeleted: true,
            PayloadJson: null,
            deletedAtUtc,
            originatingNodeId
        );
}
