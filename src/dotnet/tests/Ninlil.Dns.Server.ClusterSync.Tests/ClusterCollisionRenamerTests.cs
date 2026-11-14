using Ninlil.Dns.Server.ClusterSync.Shared;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Tests;

/// <summary>
///     This class tests <see cref="ClusterCollisionRenamer.BuildRenamedValue" />, a pure function:
///     the suffix names a real node, a long name keeps its suffix inside the 256-character
///     column, and the result is deterministic.
/// </summary>
public sealed class ClusterCollisionRenamerTests
{
    private static readonly Guid Node = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");

    [Fact]
    public void BuildRenamedValue_AShortName_AppendsTheConflictSuffixWithTheNodeIdAsThirtyTwoHexDigits()
    {
        string renamed = ClusterCollisionRenamer.BuildRenamedValue("admin", Node);

        Assert.Equal("admin-conflict-bbbbbbbb000000000000000000000001", renamed);
        Assert.Equal(renamed, ClusterCollisionRenamer.BuildRenamedValue("admin", Node));
    }

    [Fact]
    public void BuildRenamedValue_ANameTooLongForTheColumn_ShortensTheNameAndKeepsTheSuffix()
    {
        string renamed = ClusterCollisionRenamer.BuildRenamedValue(new string('x', 300), Node);

        Assert.Equal(256, renamed.Length);
        Assert.EndsWith($"-conflict-{Node:N}", renamed, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildRenamedValue_AnAllZeroNodeId_IsRefused()
    {
        Assert.Throws<ArgumentException>(() =>
            ClusterCollisionRenamer.BuildRenamedValue("admin", Guid.Empty)
        );
    }
}
