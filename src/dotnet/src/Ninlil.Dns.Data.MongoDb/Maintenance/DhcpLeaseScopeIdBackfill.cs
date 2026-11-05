using MongoDB.Bson;
using MongoDB.Driver;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Maintenance;

/// <summary>
///     This class gives each existing DHCP lease document the identifier of its scope, the
///     MongoDB counterpart of the data step in the relational <c>AddDhcpLeaseScopeId</c>
///     migration. A lease linked to its scope by name only until
///     <see cref="Ninlil.Dhcp.Data.Abstractions.Entity.LeaseEntity.ScopeId" /> existed; the
///     allocation services, the DNS registration and the lease list now find a scope's leases by
///     that identifier, so without this backfill every upgraded lease would be unreachable.
///     <para>
///         MongoDB has no migrations, so this runs at every startup. It only looks at lease
///         documents that have no <c>ScopeId</c> field at all, which only documents written
///         before the upgrade lack (the class map writes the field on every save). For each
///         such document it copies the <c>_id</c> of the scope whose name equals the lease's
///         <c>ScopeName</c>. A document whose name matches no scope belonged to a deleted scope
///         and is deleted, as in the relational migration: nothing could use it, and leaving it
///         without an identifier would let it match a lookup for an empty identifier.
///     </para>
/// </summary>
public static class DhcpLeaseScopeIdBackfill
{
    /// <summary>
    ///     This method backfills every lease document that predates the scope identifier.
    /// </summary>
    /// <param name="context">The MongoDB context whose database should be backfilled.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The number of lease documents that received an identifier or were deleted.</returns>
    public static async Task<int> RunAsync(
        NinlilDnsMongoContext context,
        CancellationToken stoppingToken = default
    )
    {
        // Raw BSON, so a missing field can be told apart from a present one.
        IMongoCollection<BsonDocument> leases = context.Database.GetCollection<BsonDocument>(
            context.Leases.CollectionNamespace.CollectionName
        );
        IMongoCollection<BsonDocument> scopes = context.Database.GetCollection<BsonDocument>(
            context.DhcpScopes.CollectionNamespace.CollectionName
        );

        FilterDefinition<BsonDocument> legacy = Builders<BsonDocument>.Filter.Exists(
            "ScopeId",
            false
        );

        if (await leases.CountDocumentsAsync(legacy, cancellationToken: stoppingToken) == 0)
            return 0;

        int changed = 0;
        List<BsonDocument> allScopes = await scopes
            .Find(FilterDefinition<BsonDocument>.Empty)
            .Project(Builders<BsonDocument>.Projection.Include("Name"))
            .ToListAsync(stoppingToken);

        foreach (BsonDocument scope in allScopes)
        {
            if (!scope.TryGetValue("Name", out BsonValue name) || !name.IsString)
                continue;

            // The scope's _id is copied as stored, so it has the Guid representation the class
            // maps read back.
            UpdateResult result = await leases.UpdateManyAsync(
                Builders<BsonDocument>.Filter.And(
                    legacy,
                    Builders<BsonDocument>.Filter.Eq("ScopeName", name)
                ),
                Builders<BsonDocument>.Update.Set("ScopeId", scope["_id"]),
                cancellationToken: stoppingToken
            );
            changed += (int)result.ModifiedCount;
        }

        DeleteResult orphans = await leases.DeleteManyAsync(legacy, stoppingToken);
        return changed + (int)orphans.DeletedCount;
    }
}
