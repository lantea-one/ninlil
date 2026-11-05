using MongoDB.Bson;

// Define our namespace.
namespace Ninlil.Dns.Data.MongoDb.Topology;

/// <summary>
///     This class computes the replication lag from a <c>replSetGetStatus</c> reply for
///     <see cref="MongoReplicaHealthProbe" />. The lag of a secondary is the time since it was
///     last current, and the result is the lag of the most delayed healthy secondary, because the
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
///             Behind: the secondary lacks the primary's newest operation. Its lag is at most the
///             wall-time gap between the two operations and at most the time since the probe first
///             saw it behind. The first bound is exact while the primary writes steadily; the
///             second keeps an idle set from reporting the gap between two no-op writes (20 seconds
///             on the pinned MongoDB 8 image) when a secondary is only a few milliseconds late.
///             The probe starts that clock at the wall time of the primary's newest operation,
///             because the secondary has been behind at least since then. Under a steady write
///             load the second bound starts near 0, so a secondary that is already behind at its
///             first check shows its lag over the next checks.
///         </item>
///     </list>
///     The status field <c>date</c> is the primary's clock, the same clock that stamps the wall
///     times, so the arithmetic needs no clock synchronization with this host.
///     <para>
///         The class keeps one timestamp per member, the time that member was first seen behind,
///         between calls. The monitor calls <see cref="Measure" /> from one loop, and the lock
///         keeps a second caller (a test, a manual check) from corrupting the dictionary.
///     </para>
/// </summary>
public sealed class MongoReplicaLag
{
    private readonly object _lock = new();
    private readonly Dictionary<string, DateTime> _behindSince = [];

    /// <summary>
    ///     This method computes the worst lag from a <c>replSetGetStatus</c> reply.
    /// </summary>
    /// <param name="status">The reply of <c>replSetGetStatus</c> from the primary.</param>
    /// <returns>
    ///     The lag in seconds, or <c>null</c> with an explanation when the set has no primary or no
    ///     healthy secondary.
    /// </returns>
    public (double? Lag, string Detail) Measure(BsonDocument status)
    {
        BsonDocument[] members =
        [
            .. status["members"].AsBsonArray.Select(member => member.AsBsonDocument),
        ];
        BsonDocument primary = members.FirstOrDefault(member => member["stateStr"] == "PRIMARY");
        if (primary is null)
            return (null, "The replica set has no primary, so the lag is unknown.");

        DateTime now = status["date"].ToUniversalTime();
        DateTime primaryApplied = AppliedTime(primary);
        double? worst = null;
        lock (_lock)
        {
            HashSet<string> behindNow = [];
            foreach (
                BsonDocument member in members.Where(member =>
                    member["stateStr"] == "SECONDARY" && member["health"].ToDouble() >= 1
                )
            )
            {
                string name = member["name"].AsString;
                double lag = 0;
                if (!IsCurrent(primary, member))
                {
                    behindNow.Add(name);
                    if (!_behindSince.TryGetValue(name, out DateTime since))
                    {
                        since = primaryApplied < now ? primaryApplied : now;
                        _behindSince[name] = since;
                    }

                    lag = Math.Max(
                        0,
                        Math.Min(
                            (primaryApplied - AppliedTime(member)).TotalSeconds,
                            (now - since).TotalSeconds
                        )
                    );
                }

                worst = Math.Max(worst ?? 0, lag);
            }

            foreach (
                string name in _behindSince.Keys.Where(name => !behindNow.Contains(name)).ToList()
            )
                _behindSince.Remove(name);
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
