using MongoDB.Bson;
using MongoDB.Driver;
using Ninlil.Dns.Data.Abstractions.Routing;
using Ninlil.Dns.Data.MongoDb.Maintenance;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Topology;

/// <summary>
///     This class measures the MongoDB secondaries for the <see cref="ReplicaHealthMonitor" />.
///     <list type="bullet">
///         <item>
///             Lag: <c>replSetGetStatus</c> lists the last applied operation of every member, and
///             <see cref="MongoReplicaLag" /> turns it into the lag of the most delayed healthy
///             secondary was last current, because the read preference may pick any of them. An
///             idle set advances its optimes only on a write or on the primary's periodic no-op
///             write, so an old optime that equals the primary's is not lag. No healthy secondary
///             is "unknown".
///         </item>
///         <item>
///             Schema: the replica view (reading with the configured read preference) holds a
///             <c>NinlilDnsSchemaSteps</c> document for every step in
///             <see cref="MongoSchemaSteps.Known" />.
///         </item>
///     </list>
///     A standalone server has no replica set status; the probe reports that as an error, which
///     the monitor treats as unknown (a read preference other than <c>Primary</c> has no effect
///     there and the composer warns about it).
/// </summary>
/// <param name="client">The MongoDB client.</param>
/// <param name="replicaView">The read-only context that reads with the configured read preference.</param>
public sealed class MongoReplicaHealthProbe(IMongoClient client, NinlilDnsMongoContext replicaView)
    : IReplicaHealthProbe
{
    /// <inheritdoc />
    public async Task<ReplicaProbeResult> ProbeAsync(CancellationToken stoppingToken)
    {
        BsonDocument status = await client
            .GetDatabase("admin")
            .RunCommandAsync<BsonDocument>(
                new BsonDocument("replSetGetStatus", 1),
                ReadPreference.Primary,
                stoppingToken
            );

        (double? lag, string detail) = MongoReplicaLag.Measure(status);

        List<string> stored = (
            await replicaView
                .SchemaSteps.Find(FilterDefinition<BsonDocument>.Empty)
                .ToListAsync(stoppingToken)
        )
            .Select(document => document["_id"].ToString())
            .ToList();
        string[] missing = [.. MongoSchemaSteps.Known.Where(step => !stored.Contains(step))];
        bool schemaCurrent = missing.Length == 0;
        if (!schemaCurrent)
            detail =
                "The replica is missing schema steps the primary has: "
                + $"{string.Join(", ", missing)}.";

        return new ReplicaProbeResult(lag, schemaCurrent, detail);
    }
}
