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

    // BSON dates keep whole milliseconds, so lag expectations compare to two decimals.
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
        (double? seconds, _) = MongoReplicaLag.Measure(
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
        (double? seconds, _) = MongoReplicaLag.Measure(
            Status(30, Member("p", "PRIMARY", 120, 20.1), Member("s", "SECONDARY", 100, 20.1))
        );

        Assert.Equal(0, seconds);
    }

    /// <summary>
    ///     A secondary a few milliseconds late after an idle no-op write is reported behind by the
    ///     time since its previous operation at that one check and current at the next.
    /// </summary>
    [Fact]
    public void Measure_WhenAnIdleSecondaryIsAMomentLate_IsBehindOnceThenCurrent()
    {
        BsonDocument primary = Member("p", "PRIMARY", 120, 20.1);

        (double? first, _) = MongoReplicaLag.Measure(
            Status(20.2, primary, Member("s", "SECONDARY", 100, 0.1))
        );
        (double? next, _) = MongoReplicaLag.Measure(
            Status(22.2, primary, Member("s", "SECONDARY", 120, 20.1))
        );

        Assert.Equal(20.1, first.Value, 2);
        Assert.Equal(0, next);
    }

    /// <summary>
    ///     A secondary far behind at the first check is never reported below its lag, and it
    ///     stays that way at later checks.
    /// </summary>
    [Fact]
    public void Measure_WhenASecondaryIsFarBehindAtTheFirstCheck_ReportsItsWholeLag()
    {
        BsonDocument secondary = Member("s", "SECONDARY", 1000, 1000);

        (double? first, _) = MongoReplicaLag.Measure(
            Status(1600.1, Member("p", "PRIMARY", 1600, 1600), secondary)
        );
        (double? second, _) = MongoReplicaLag.Measure(
            Status(1605.1, Member("p", "PRIMARY", 1605, 1605), secondary)
        );

        Assert.Equal(600.1, first.Value, 2);
        Assert.Equal(605.1, second.Value, 2);
    }

    /// <summary>
    ///     A catch-up brings the lag back to 0.
    /// </summary>
    [Fact]
    public void Measure_WhenASecondaryCatchesUp_IsZeroAgain()
    {
        BsonDocument primary = Member("p", "PRIMARY", 120, 20.1);

        (double? behind, _) = MongoReplicaLag.Measure(
            Status(26, primary, Member("s", "SECONDARY", 100, 0.1))
        );
        (double? current, _) = MongoReplicaLag.Measure(
            Status(27, primary, Member("s", "SECONDARY", 120, 20.1))
        );

        Assert.Equal(25.9, behind.Value, 2);
        Assert.Equal(0, current);
    }

    /// <summary>
    ///     Under steady writes the lag is the time since the secondary's last operation.
    /// </summary>
    [Fact]
    public void Measure_UnderSteadyWrites_IsTheTimeSinceTheSecondarysLastOperation()
    {
        (double? seconds, _) = MongoReplicaLag.Measure(
            Status(110.1, Member("p", "PRIMARY", 111, 110.0), Member("s", "SECONDARY", 105, 104.0))
        );

        Assert.Equal(6.1, seconds.Value, 2);
    }

    /// <summary>
    ///     A secondary that misses one write after which the primary idles is behind by the time
    ///     since its own last operation, not by the few milliseconds between the two operations.
    /// </summary>
    [Fact]
    public void Measure_WhenASecondaryMissesOneWriteBeforeAnIdlePause_IsBehindByTheTimeSinceItsLastOperation()
    {
        (double? seconds, _) = MongoReplicaLag.Measure(
            Status(45, Member("p", "PRIMARY", 130, 30.0), Member("s", "SECONDARY", 129, 29.99))
        );

        Assert.Equal(15.01, seconds.Value, 2);
    }

    /// <summary>
    ///     The most delayed healthy secondary decides; an unhealthy one is ignored.
    /// </summary>
    [Fact]
    public void Measure_WithSeveralSecondaries_IsTheWorstHealthyOne()
    {
        (double? seconds, _) = MongoReplicaLag.Measure(
            Status(
                104,
                Member("p", "PRIMARY", 100, 99.9),
                Member("a", "SECONDARY", 100, 99.9),
                Member("b", "SECONDARY", 90, 80),
                Member("c", "SECONDARY", 50, 10, health: 0)
            )
        );

        Assert.Equal(24, seconds.Value, 3);
    }

    /// <summary>
    ///     Without a primary or without a healthy secondary the lag is unknown.
    /// </summary>
    [Fact]
    public void Measure_WithoutAPrimaryOrAHealthySecondary_IsUnknown()
    {
        (double? noPrimary, string noPrimaryDetail) = MongoReplicaLag.Measure(
            Status(10, Member("s", "SECONDARY", 100, 1))
        );
        (double? noSecondary, string noSecondaryDetail) = MongoReplicaLag.Measure(
            Status(10, Member("p", "PRIMARY", 100, 1), Member("s", "SECONDARY", 100, 1, health: 0))
        );

        Assert.Null(noPrimary);
        Assert.Contains("no primary", noPrimaryDetail);
        Assert.Null(noSecondary);
        Assert.Contains("no healthy secondary", noSecondaryDetail);
    }

    /// <summary>
    ///     A reading taken in the few milliseconds after an idle no-op write shows the secondary
    ///     behind by the time since its previous operation. The second reading, taken after the
    ///     settle delay, finds it current, and that one is the result.
    /// </summary>
    [Fact]
    public async Task MeasureSettledAsync_WhenTheSecondaryCatchesUpDuringTheDelay_IsZero()
    {
        BsonDocument primary = Member("p", "PRIMARY", 120, 20.1);
        Queue<BsonDocument> replies = new([
            Status(20.2, primary, Member("s", "SECONDARY", 100, 0.1)),
            Status(21.3, primary, Member("s", "SECONDARY", 120, 20.1)),
        ]);

        (double? seconds, _) = await MongoReplicaLag.MeasureSettledAsync(
            _ => Task.FromResult(replies.Dequeue()),
            TimeSpan.Zero,
            CancellationToken.None
        );

        Assert.Equal(0, seconds);
        Assert.Empty(replies);
    }

    /// <summary>
    ///     A secondary that is still behind at the second reading is reported with the lag of
    ///     that reading, so a stuck replica is never made eligible by the second look.
    /// </summary>
    [Fact]
    public async Task MeasureSettledAsync_WhenTheSecondaryStaysBehind_ReportsTheSecondReading()
    {
        BsonDocument primary = Member("p", "PRIMARY", 120, 20.1);
        BsonDocument secondary = Member("s", "SECONDARY", 100, 0.1);
        Queue<BsonDocument> replies = new([
            Status(20.2, primary, secondary),
            Status(21.2, primary, secondary),
        ]);

        (double? seconds, _) = await MongoReplicaLag.MeasureSettledAsync(
            _ => Task.FromResult(replies.Dequeue()),
            TimeSpan.Zero,
            CancellationToken.None
        );

        Assert.Equal(21.1, seconds.Value, 2);
    }

    /// <summary>
    ///     A reading that is current, or behind by less than a second, is final: a healthy
    ///     replica under steady writes costs no second read and no wait.
    /// </summary>
    [Fact]
    public async Task MeasureSettledAsync_WhenTheFirstReadingIsCurrentOrWithinASecond_ReadsOnce()
    {
        int reads = 0;
        BsonDocument Reply(CancellationToken _)
        {
            reads++;
            return Status(
                110.5,
                Member("p", "PRIMARY", 111, 110.0),
                Member("s", "SECONDARY", 110, 110.0 - 0.2)
            );
        }

        (double? seconds, _) = await MongoReplicaLag.MeasureSettledAsync(
            token => Task.FromResult(Reply(token)),
            TimeSpan.FromSeconds(30),
            CancellationToken.None
        );

        Assert.Equal(1, reads);
        Assert.Equal(0.7, seconds.Value, 2);
    }
}
