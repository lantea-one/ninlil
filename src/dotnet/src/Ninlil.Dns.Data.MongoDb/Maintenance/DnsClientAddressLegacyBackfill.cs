using MongoDB.Bson;
using MongoDB.Driver;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Maintenance;

/// <summary>
///     This class moves the single identifier of a legacy known-client document into the
///     <c>NinlilDnsClientAddresses</c> collection. It is the MongoDB counterpart of the data copy
///     in the relational <c>AddDnsClientAddresses</c> migration. Legacy device documents hold one
///     identifier as top-level <c>ClientIdentifier</c>/<c>ProtocolVersion</c> fields, which the
///     <see cref="Abstractions.Entity.DnsClientEntity" /> class map no longer has (extra elements
///     are ignored, so the documents still load). Identity lookups go through the address
///     collection, so without this backfill an upgraded MongoDB installation would lose every
///     device's identity.
///     <para>
///         MongoDB has no migrations, so this runs at every startup and is idempotent. It only
///         touches documents that still have the legacy <c>ClientIdentifier</c> field and removes
///         that field (and <c>ProtocolVersion</c>) once the address document exists. The device's
///         <c>_id</c> is reused as the address document's <c>_id</c>, which is unique because a
///         legacy device had one identifier. <c>CreatedAtUtc</c>, <c>ProtocolVersion</c> and
///         <c>OriginatingNodeId</c> are copied as raw BSON, so they are serialized as the address
///         class map would write them.
///     </para>
/// </summary>
public static class DnsClientAddressLegacyBackfill
{
    /// <summary>
    ///     This method backfills every device document that still has the legacy identifier.
    /// </summary>
    /// <param name="context">The MongoDB context whose database should be backfilled.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The number of legacy device documents migrated.</returns>
    public static async Task<int> RunAsync(
        NinlilDnsMongoContext context,
        CancellationToken stoppingToken = default
    )
    {
        // Raw BSON, because the typed class maps do not know the legacy fields.
        IMongoCollection<BsonDocument> clients = context.Database.GetCollection<BsonDocument>(
            context.DnsClients.CollectionNamespace.CollectionName
        );
        IMongoCollection<BsonDocument> addresses = context.Database.GetCollection<BsonDocument>(
            context.DnsClientAddresses.CollectionNamespace.CollectionName
        );

        FilterDefinition<BsonDocument> legacyFilter = Builders<BsonDocument>.Filter.Exists(
            "ClientIdentifier"
        );
        List<BsonDocument> legacyClients = await clients
            .Find(legacyFilter)
            .ToListAsync(stoppingToken);

        foreach (BsonDocument client in legacyClients)
        {
            BsonValue createdAtUtc = client.GetValue("CreatedAtUtc", BsonNull.Value);

            // Address and LastSeenUtc start unset, as for a newly registered identifier.
            BsonDocument address = new()
            {
                { "_id", client["_id"] },
                { "ClientId", client["_id"] },
                { "ClientIdentifier", client["ClientIdentifier"] },
                { "ProtocolVersion", client.GetValue("ProtocolVersion", BsonNull.Value) },
                { "Address", BsonNull.Value },
                { "FirstSeenUtc", createdAtUtc },
                { "LastSeenUtc", BsonNull.Value },
                { "CreatedAtUtc", createdAtUtc },
                { "UpdatedAtUtc", BsonNull.Value },
                { "OriginatingNodeId", client.GetValue("OriginatingNodeId", BsonNull.Value) },
            };

            // Upsert, so a run interrupted before the unset below completes on a later startup
            // instead of failing on a duplicate key.
            await addresses.ReplaceOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", client["_id"]),
                address,
                new ReplaceOptions { IsUpsert = true },
                stoppingToken
            );

            // Remove the legacy fields so the device is not migrated again.
            await clients.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", client["_id"]),
                Builders<BsonDocument>.Update.Unset("ClientIdentifier").Unset("ProtocolVersion"),
                cancellationToken: stoppingToken
            );
        }

        return legacyClients.Count;
    }
}
