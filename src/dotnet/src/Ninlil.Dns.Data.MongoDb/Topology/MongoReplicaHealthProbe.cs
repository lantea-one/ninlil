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
///             Lag: <c>replSetGetStatus</c> lists the optime of every member. The lag is the
///             primary's optime minus the optime of its most delayed healthy secondary, because the
///             read preference may pick any of them. Optimes have whole-second resolution, so a
///             current secondary can read as 1 second behind. An idle primary still writes a no-op entry to
///             its oplog about every 10 seconds, so a stuck secondary falls behind on the next one
///             and the detection takes up to that long. No healthy secondary is "unknown".
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

        (double? lag, string detail) = Lag(status);

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

    /// <summary>
    ///     This method computes the worst lag from a <c>replSetGetStatus</c> reply.
    /// </summary>
    private static (double? Lag, string Detail) Lag(BsonDocument status)
    {
        BsonDocument[] members =
        [
            .. status["members"].AsBsonArray.Select(member => member.AsBsonDocument),
        ];
        BsonDocument primary = members.FirstOrDefault(member => member["stateStr"] == "PRIMARY");
        if (primary is null)
            return (null, "The replica set has no primary, so the lag is unknown.");

        DateTime primaryOptime = primary["optimeDate"].ToUniversalTime();
        double? worst = null;
        foreach (
            BsonDocument member in members.Where(member =>
                member["stateStr"] == "SECONDARY" && member["health"].ToDouble() >= 1
            )
        )
        {
            double lag = Math.Max(
                0,
                (primaryOptime - member["optimeDate"].ToUniversalTime()).TotalSeconds
            );
            worst = Math.Max(worst ?? 0, lag);
        }

        return worst is null
            ? (null, "The replica set has no healthy secondary, so the lag is unknown.")
            : (worst, "");
    }
}
