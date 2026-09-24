using Ninlil.Dns.Server.Core.ClusterSync;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Tests;

/// <summary>
///     This class proves <see cref="ClusterSyncConflictResolver.IncomingWins" /> implements
///     the cluster's own last-write-wins rule exactly as documented: a strictly later timestamp
///     always wins, and an exact tie is broken deterministically by comparing the two originating
///     node ids' own canonical string form, ordinally.
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
    ///     This test is the genuine two-conflicting-changes proof: two changes carrying different
    ///     timestamps, confirming the later one wins regardless of which one this method is asked about
    ///     "first" (the resolver itself is stateless and order-independent, so calling it with
    ///     either change cast as "incoming" against the other as "local" must agree on the same,
    ///     single winner).
    /// </summary>
    [Fact]
    public void TheLaterOfTwoConflictingChangesWinsRegardlessOfWhichIsTreatedAsIncoming()
    {
        DateTimeOffset earlier = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        DateTimeOffset later = DateTimeOffset.Parse("2026-01-01T00:00:05Z");

        // Ask twice, in both directions -- the earlier change is never allowed to win over the
        // later one, whichever role ("local" already on record, or "incoming" just arrived) each
        // one plays.
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

        // "bbbbbbbb-..." sorts after "aaaaaaaa-..." ordinally, so the incoming change (from Node
        // B) should win a tie against a local change already recorded from Node A.
        bool incomingFromBWinsOverLocalFromA = ClusterSyncConflictResolver.IncomingWins(
            sameInstant,
            NodeA,
            sameInstant,
            NodeB
        );

        // The reverse direction must be consistent: an incoming change from Node A never beats a
        // local change already recorded from Node B on the same exact tie.
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

        // A null local originating node id (an entity that predates cluster config sync, or a
        // still-single-node deployment) is treated as Guid.Empty for the tie-break -- any real,
        // non-empty incoming node id sorts after it ordinally.
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

        // A genuinely identical envelope (the same node re-delivering, or a duplicate broadcast)
        // must never be treated as "incoming wins" -- there is nothing to apply again.
        bool result = ClusterSyncConflictResolver.IncomingWins(
            sameInstant,
            NodeA,
            sameInstant,
            NodeA
        );

        Assert.False(result);
    }
}
