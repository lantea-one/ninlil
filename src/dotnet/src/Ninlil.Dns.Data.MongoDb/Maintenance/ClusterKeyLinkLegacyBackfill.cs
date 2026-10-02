using MongoDB.Bson;
using MongoDB.Driver;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Maintenance;

/// <summary>
///     This class links each existing cluster peer's TSIG key to its peer, the MongoDB
///     counterpart of the data step in the relational <c>LinkClusterKeysToPeers</c> migration.
///     The cluster channel accepts only keys whose <c>ClusterNodeId</c> points at a known peer
///     (see <see cref="Abstractions.Entity.TsigKeyEntity.ClusterNodeId" />); peers joined before
///     that field existed refer to their key by name only, so without this backfill an upgraded
///     MongoDB cluster would stop talking.
///     <para>
///         MongoDB has no migrations, so this runs at every startup. It only looks at key
///         documents that have no <c>ClusterNodeId</c> field at all, which only documents written
///         before the upgrade lack (the class map writes the field, null or not, on every save),
///         and gives each one the field: the id of the peer naming the key, or null. A key
///         created later under a peer's key name therefore never gets linked.
///     </para>
/// </summary>
public static class ClusterKeyLinkLegacyBackfill
{
    /// <summary>
    ///     This method backfills every key document that predates the cluster key link.
    /// </summary>
    /// <param name="context">The MongoDB context whose database should be backfilled.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The number of keys linked to a peer.</returns>
    public static async Task<int> RunAsync(
        NinlilDnsMongoContext context,
        CancellationToken stoppingToken = default
    )
    {
        // Raw BSON, so a missing field can be told apart from a null one.
        IMongoCollection<BsonDocument> keys = context.Database.GetCollection<BsonDocument>(
            context.TsigKeys.CollectionNamespace.CollectionName
        );
        IMongoCollection<BsonDocument> nodes = context.Database.GetCollection<BsonDocument>(
            context.ClusterNodes.CollectionNamespace.CollectionName
        );

        List<BsonDocument> legacyKeys = await keys.Find(
                Builders<BsonDocument>.Filter.Exists("ClusterNodeId", false)
            )
            .ToListAsync(stoppingToken);

        int linked = 0;
        foreach (BsonDocument key in legacyKeys)
        {
            BsonDocument node = await nodes
                .Find(
                    Builders<BsonDocument>.Filter.Eq(
                        "TsigKeyName",
                        key.GetValue("Name", BsonNull.Value)
                    )
                )
                .FirstOrDefaultAsync(stoppingToken);

            // The peer's _id is copied as stored, so it has the Guid representation the class
            // maps read back.
            BsonValue clusterNodeId = node?["_id"] ?? BsonNull.Value;
            if (node is not null)
                linked++;

            await keys.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", key["_id"]),
                Builders<BsonDocument>.Update.Set("ClusterNodeId", clusterNodeId),
                cancellationToken: stoppingToken
            );
        }

        return linked;
    }
}
