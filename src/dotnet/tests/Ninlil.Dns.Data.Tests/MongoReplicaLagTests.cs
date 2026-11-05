using MongoDB.Bson;
using Ninlil.Dns.Data.MongoDb.Topology;

// Define our namespace.
namespace Ninlil.Dns.Data.Tests;

/// <summary>
///     This class tests <see cref="MongoReplicaLag" /> with crafted <c>replSetGetStatus</c>
///     replies, so the lag rules run without a MongoDB server. The status time is the primary's
///     clock; the helpers build members the way MongoDB 8 reports them.
/// </summary>
public sealed class MongoReplicaLagTests
{
    private static readonly DateTime Base = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static BsonDocument Member(
        string name,
        string state,
        int optimeSecond,
        double wallSeconds,
        double health = 1
    ) =>
        new()
        {
            { "name", name },
            { "health", health },
            { "stateStr", state },
            {
                "optime",
                new BsonDocument { { "ts", new BsonTimestamp(optimeSecond, 1) }, { "t", 1L } }
            },
            { "optimeDate", Base.AddSeconds(Math.Floor(wallSeconds)) },
            { "lastAppliedWallTime", Base.AddSeconds(wallSeconds) },
        };

    private static BsonDocument Status(double nowSeconds, params BsonDocument[] members) =>
        new() { { "date", Base.AddSeconds(nowSeconds) }, { "members", new BsonArray(members) } };

    /// <summary>
    ///     An old operation that both members share is not lag on an idle set.
    /// </summary>
    [Fact]
    public void Measure_WithEqualOptimesAndAnOldOperation_IsZero()
    {
        MongoReplicaLag lag = new();

        (double? seconds, _) = lag.Measure(
            Status(600, Member("p", "PRIMARY", 100, 100.2), Member("s", "SECONDARY", 100, 100.2))
        );

        Assert.Equal(0, seconds);
    }

    /// <summary>
    ///     The wall time reaches the primary's before the secondary's reported optime does, and
    ///     that counts as current.
    /// </summary>
    [Fact]
    public void Measure_WithEqualWallTimesAndAStaleOptime_IsZero()
    {
        MongoReplicaLag lag = new();

        (double? seconds, _) = lag.Measure(
            Status(30, Member("p", "PRIMARY", 120, 20.1), Member("s", "SECONDARY", 100, 20.1))
        );

        Assert.Equal(0, seconds);
    }

    /// <summary>
    ///     A secondary that lacks the newest operation of an idle set is not credited with the
    ///     20-second gap between the two operations; the clock starts at the newest operation.
    /// </summary>
    [Fact]
    public void Measure_WhenAnIdleSecondaryIsAMomentLate_IsTheTimeSinceTheNewestOperation()
    {
        MongoReplicaLag lag = new();

        (double? seconds, _) = lag.Measure(
            Status(20.3, Member("p", "PRIMARY", 120, 20.1), Member("s", "SECONDARY", 100, 0.1))
        );

        Assert.Equal(0.2, seconds.Value, 3);
    }

    /// <summary>
    ///     A stuck secondary falls further behind on every check, measured from when it was first
    ///     seen behind, and a catch-up resets the clock.
    /// </summary>
    [Fact]
    public void Measure_WhenASecondaryStaysBehind_GrowsUntilItIsCurrentAgain()
    {
        MongoReplicaLag lag = new();
        BsonDocument stuck = Member("s", "SECONDARY", 100, 0.1);

        (double? first, _) = lag.Measure(Status(20.3, Member("p", "PRIMARY", 120, 20.1), stuck));
        (double? second, _) = lag.Measure(Status(26, Member("p", "PRIMARY", 120, 20.1), stuck));
        (double? current, _) = lag.Measure(
            Status(27, Member("p", "PRIMARY", 120, 20.1), Member("s", "SECONDARY", 120, 20.1))
        );
        (double? behindAgain, _) = lag.Measure(
            Status(41, Member("p", "PRIMARY", 140, 40.1), stuck)
        );

        Assert.Equal(0.2, first.Value, 3);
        Assert.Equal(5.9, second.Value, 3);
        Assert.Equal(0, current);
        Assert.Equal(0.9, behindAgain.Value, 3);
    }

    /// <summary>
    ///     Under steady writes the gap between the two operations is the lag, and it is not more
    ///     than that even when the secondary has been behind for longer.
    /// </summary>
    [Fact]
    public void Measure_UnderSteadyWrites_IsTheGapBetweenTheOperations()
    {
        MongoReplicaLag lag = new();
        BsonDocument secondary = Member("s", "SECONDARY", 100, 99.5);

        lag.Measure(Status(100.1, Member("p", "PRIMARY", 101, 100.0), secondary));
        (double? seconds, _) = lag.Measure(
            Status(110.1, Member("p", "PRIMARY", 111, 110.0), Member("s", "SECONDARY", 105, 104.0))
        );

        Assert.Equal(6, seconds.Value, 3);
    }

    /// <summary>
    ///     The most delayed healthy secondary decides; an unhealthy one is ignored.
    /// </summary>
    [Fact]
    public void Measure_WithSeveralSecondaries_IsTheWorstHealthyOne()
    {
        MongoReplicaLag lag = new();
        lag.Measure(
            Status(
                100,
                Member("p", "PRIMARY", 100, 99.9),
                Member("a", "SECONDARY", 90, 90),
                Member("b", "SECONDARY", 90, 80)
            )
        );

        (double? seconds, _) = lag.Measure(
            Status(
                104,
                Member("p", "PRIMARY", 100, 99.9),
                Member("a", "SECONDARY", 100, 99.9),
                Member("b", "SECONDARY", 90, 80),
                Member("c", "SECONDARY", 50, 10, health: 0)
            )
        );

        Assert.Equal(4.1, seconds.Value, 3);
    }

    /// <summary>
    ///     Without a primary or without a healthy secondary the lag is unknown.
    /// </summary>
    [Fact]
    public void Measure_WithoutAPrimaryOrAHealthySecondary_IsUnknown()
    {
        MongoReplicaLag lag = new();

        (double? noPrimary, string noPrimaryDetail) = lag.Measure(
            Status(10, Member("s", "SECONDARY", 100, 1))
        );
        (double? noSecondary, string noSecondaryDetail) = lag.Measure(
            Status(10, Member("p", "PRIMARY", 100, 1), Member("s", "SECONDARY", 100, 1, health: 0))
        );

        Assert.Null(noPrimary);
        Assert.Contains("no primary", noPrimaryDetail);
        Assert.Null(noSecondary);
        Assert.Contains("no healthy secondary", noSecondaryDetail);
    }
}
