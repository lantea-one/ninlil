using MongoDB.Bson;
using MongoDB.Driver;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Maintenance;

/// <summary>
///     This class moves a legacy known-client document's own single identifier into the
///     <c>NinlilDnsClientAddresses</c> collection -- the MongoDB counterpart of the relational
///     <c>AddDnsClientAddresses</c> migration's own data-copy step. A device document written before
///     a device could carry several identifiers stored its one identifier directly, as top-level
///     <c>ClientIdentifier</c>/<c>ProtocolVersion</c> fields; the current
///     <see cref="Abstractions.Entity.DnsClientEntity" /> class map no longer has either property
///     (the class-map conventions ignore extra elements, so such a document still loads), and
///     every identity lookup now resolves through the address collection instead. Without this
///     backfill, an upgraded MongoDB installation would silently lose every known device's
///     identity.
///     <para>
///         MongoDB has no migration system, so this runs once at every startup against the active
///         MongoDB database and is idempotent: it only ever touches documents that still carry the
///         legacy <c>ClientIdentifier</c> field, and removes that field (and
///         <c>ProtocolVersion</c>) once the address document exists, so a later startup finds
///         nothing left to do. The device's own <c>_id</c> is reused as its one address document's
///         <c>_id</c> -- unique by construction, since a legacy device carried exactly one
///         identifier -- and its <c>CreatedAtUtc</c>/<c>ProtocolVersion</c>/
///         <c>OriginatingNodeId</c> values are copied across as raw BSON, so their serialized
///         shape exactly matches what the address class map itself would have written.
///     </para>
/// </summary>
public static class DnsClientAddressLegacyBackfill
{
    /// <summary>
    ///     This method backfills every legacy device document still carrying its own identifier.
    /// </summary>
    /// <param name="context">The MongoDB context whose database should be backfilled.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The number of legacy device documents migrated.</returns>
    public static async Task<int> RunAsync(
        NinlilDnsMongoContext context,
        CancellationToken stoppingToken = default
    )
    {
        // Work against raw BSON documents, since the typed class maps no longer know about the
        // legacy fields at all.
        IMongoCollection<BsonDocument> clients = context.Database.GetCollection<BsonDocument>(
            context.DnsClients.CollectionNamespace.CollectionName
        );
        IMongoCollection<BsonDocument> addresses = context.Database.GetCollection<BsonDocument>(
            context.DnsClientAddresses.CollectionNamespace.CollectionName
        );

        // Only a document still carrying the legacy identifier field needs migrating.
        FilterDefinition<BsonDocument> legacyFilter = Builders<BsonDocument>.Filter.Exists(
            "ClientIdentifier"
        );
        List<BsonDocument> legacyClients = await clients
            .Find(legacyFilter)
            .ToListAsync(stoppingToken);

        foreach (BsonDocument client in legacyClients)
        {
            BsonValue createdAtUtc = client.GetValue("CreatedAtUtc", BsonNull.Value);

            // Build the address document from the device's own raw values. Address and
            // LastSeenUtc start unset, exactly as a freshly registered identifier's would.
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

            // Upsert rather than insert, so a run interrupted between this write and the unset
            // below simply converges on a later startup instead of failing on a duplicate key.
            await addresses.ReplaceOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", client["_id"]),
                address,
                new ReplaceOptions { IsUpsert = true },
                stoppingToken
            );

            // Strip the legacy fields so this device is never migrated again.
            await clients.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", client["_id"]),
                Builders<BsonDocument>.Update.Unset("ClientIdentifier").Unset("ProtocolVersion"),
                cancellationToken: stoppingToken
            );
        }

        return legacyClients.Count;
    }
}
