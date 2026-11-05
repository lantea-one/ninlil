using MongoDB.Bson;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Topology;

/// <summary>
///     This class computes the replication lag from a <c>replSetGetStatus</c> reply for
///     <see cref="MongoReplicaHealthProbe" />. The lag of a secondary is how far its last applied
///     operation trails the primary's, and the result is the lag of the most delayed healthy secondary, because the
///     read preference may pick any of them.
///     <list type="bullet">
///         <item>
///             Current: the secondary's last applied operation is the primary's. The members'
///             <c>optime</c> (<c>ts</c> and term <c>t</c>) is compared in full, and so is
///             <c>lastAppliedWallTime</c>, the primary's wall-clock time of that operation with
///             millisecond resolution (<c>optimeDate</c> has whole seconds). Either match means
///             current: the wall time reaches the primary's before the <c>optime</c> shown for the
///             secondary does (that one follows the next heartbeat, up to 2 seconds later). An
///             idle set writes only a periodic no-op, so a matching operation can be old, and the
///             lag is 0 however old it is. (MongoDB documentation, "replSetGetStatus": a member's
///             <c>optime</c> is the last operation applied, and <c>lastAppliedWallTime</c> is the
///             wall-clock time of that operation.)
///         </item>
///         <item>
///             Behind: the secondary lacks the primary's newest operation. Its lag is the wall-time
///             gap between the primary's last applied operation and the secondary's (the
///             <c>optimeDate</c> gap when a status has no wall times). That is an upper bound of
///             the true lag: the secondary has been behind since the primary wrote the first
///             operation the secondary lacks, which is no earlier than the secondary's own last
///             operation. The probe keeps no state between checks, so a secondary that is far
///             behind at the first check, a new member or a restarted monitor is never reported
///             below its lag; a stale replica serving reads is worse than a brief fallback to the
///             primary. The cost: a secondary a few milliseconds late right after the primary's
///             idle no-op write reads as behind by the gap between two no-op writes (20 seconds
///             on the pinned MongoDB 8 image) for that one check, and is current at the next.
///         </item>
///     </list>
///     The wall times are the primary's clock, so the arithmetic needs no clock synchronization
///     with this host.
/// </summary>
public static class MongoReplicaLag
{
    /// <summary>
    ///     This method computes the worst lag from a <c>replSetGetStatus</c> reply.
    /// </summary>
    /// <param name="status">The reply of <c>replSetGetStatus</c> from the primary.</param>
    /// <returns>
    ///     The lag in seconds, or <c>null</c> with an explanation when the set has no primary or no
    ///     healthy secondary.
    /// </returns>
    public static (double? Lag, string Detail) Measure(BsonDocument status)
    {
        BsonDocument[] members =
        [
            .. status["members"].AsBsonArray.Select(member => member.AsBsonDocument),
        ];
        BsonDocument primary = members.FirstOrDefault(member => member["stateStr"] == "PRIMARY");
        if (primary is null)
            return (null, "The replica set has no primary, so the lag is unknown.");

        DateTime primaryApplied = AppliedTime(primary);
        double? worst = null;
        foreach (
            BsonDocument member in members.Where(member =>
                member["stateStr"] == "SECONDARY" && member["health"].ToDouble() >= 1
            )
        )
        {
            double lag = IsCurrent(primary, member)
                ? 0
                : Math.Max(0, (primaryApplied - AppliedTime(member)).TotalSeconds);
            worst = Math.Max(worst ?? 0, lag);
        }

        return worst is null
            ? (null, "The replica set has no healthy secondary, so the lag is unknown.")
            : (worst, "");
    }

    private static bool IsCurrent(BsonDocument primary, BsonDocument secondary)
    {
        if (primary.Contains("optime") && primary["optime"] == secondary.GetValue("optime", null))
            return true;

        return primary.Contains("lastAppliedWallTime")
            && secondary.Contains("lastAppliedWallTime")
            && primary["lastAppliedWallTime"] == secondary["lastAppliedWallTime"];
    }

    private static DateTime AppliedTime(BsonDocument member) =>
        (
            member.Contains("lastAppliedWallTime")
                ? member["lastAppliedWallTime"]
                : member["optimeDate"]
        ).ToUniversalTime();
}
