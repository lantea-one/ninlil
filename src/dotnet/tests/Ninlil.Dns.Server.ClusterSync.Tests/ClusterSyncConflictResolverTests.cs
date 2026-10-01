using Ninlil.Dns.Server.Core.ClusterSync;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Tests;

/// <summary>
///     This class tests <see cref="ClusterSyncConflictResolver.IncomingWins" />, a pure function:
///     a later timestamp wins, and on an equal timestamp the change whose originating node id
///     (as a "D" string, a missing one counting as <see cref="Guid.Empty" />) sorts later
///     ordinally wins. An identical change never wins.
/// </summary>
public class ClusterSyncConflictResolverTests
{
    private static readonly Guid NodeA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000000");
    private static readonly Guid NodeB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000000");

    [Fact]
    public void IncomingWinsReturnsTrueWhenTheIncomingTimestampIsStrictlyLater()
    {
        DateTimeOffset local = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        DateTimeOffset incoming = local.AddSeconds(1);

        bool result = ClusterSyncConflictResolver.IncomingWins(local, NodeA, incoming, NodeB);

        Assert.True(result);
    }

    [Fact]
    public void IncomingWinsReturnsFalseWhenTheIncomingTimestampIsStrictlyEarlier()
    {
        DateTimeOffset local = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        DateTimeOffset incoming = local.AddSeconds(-1);

        bool result = ClusterSyncConflictResolver.IncomingWins(local, NodeA, incoming, NodeB);

        Assert.False(result);
    }

    /// <summary>
    ///     This test checks two changes with different timestamps in both directions: whichever
    ///     plays "incoming" and whichever "local", the later one wins.
    /// </summary>
    [Fact]
    public void TheLaterOfTwoConflictingChangesWinsRegardlessOfWhichIsTreatedAsIncoming()
    {
        DateTimeOffset earlier = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        DateTimeOffset later = DateTimeOffset.Parse("2026-01-01T00:00:05Z");

        // Ask in both directions; the earlier change never wins.
        bool laterWinsWhenIncoming = ClusterSyncConflictResolver.IncomingWins(
            earlier,
            NodeA,
            later,
            NodeB
        );
        bool earlierWinsWhenIncoming = ClusterSyncConflictResolver.IncomingWins(
            later,
            NodeB,
            earlier,
            NodeA
        );

        Assert.True(laterWinsWhenIncoming);
        Assert.False(earlierWinsWhenIncoming);
    }

    [Fact]
    public void ExactTimestampTieIsBrokenByComparingNodeIdsOrdinallyWithTheGreaterWinning()
    {
        DateTimeOffset sameInstant = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

        // "bbbbbbbb-..." sorts after "aaaaaaaa-...", so the incoming change from node B wins a
        // tie against a local change from node A.
        bool incomingFromBWinsOverLocalFromA = ClusterSyncConflictResolver.IncomingWins(
            sameInstant,
            NodeA,
            sameInstant,
            NodeB
        );

        // And the reverse: an incoming change from node A does not beat a local one from node B.
        bool incomingFromALosesToLocalFromB = ClusterSyncConflictResolver.IncomingWins(
            sameInstant,
            NodeB,
            sameInstant,
            NodeA
        );

        Assert.True(incomingFromBWinsOverLocalFromA);
        Assert.False(incomingFromALosesToLocalFromB);
    }

    [Fact]
    public void ExactTimestampTieAgainstNoRecordedLocalOriginAlwaysLetsTheIncomingChangeWin()
    {
        DateTimeOffset sameInstant = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

        // A null local node id (for example a row written outside the sync-aware paths) counts
        // as Guid.Empty, which any non-empty incoming node id sorts after.
        bool result = ClusterSyncConflictResolver.IncomingWins(
            sameInstant,
            null,
            sameInstant,
            NodeA
        );

        Assert.True(result);
    }

    [Fact]
    public void IdenticalTimestampAndNodeIdIsNotTreatedAsAWin()
    {
        DateTimeOffset sameInstant = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

        // An identical envelope (a duplicate delivery) must not win; there is nothing to apply.
        bool result = ClusterSyncConflictResolver.IncomingWins(
            sameInstant,
            NodeA,
            sameInstant,
            NodeA
        );

        Assert.False(result);
    }
}
