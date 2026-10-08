using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Server.Backup.Catalog;

// Define our namespace.
namespace Ninlil.Dns.Server.Backup.Tests;

/// <summary>
///     This class tests <see cref="BackupLegacyFixups.LinkClusterKeys" />.
/// </summary>
public sealed class BackupLegacyFixupsTests
{
    private static readonly Guid NodeId = Guid.NewGuid();

    private static ClusterNodeEntity Node(string keyName) =>
        new() { Id = NodeId, TsigKeyName = keyName };

    [Fact]
    public void AKeyWithoutTheClusterNodeIdPropertyIsLinkedToItsPeerAndTheCountIsReturned()
    {
        List<TsigKeyEntity> keys = [new() { Name = "peer-key" }, new() { Name = "invite-key" }];

        int linked = BackupLegacyFixups.LinkClusterKeys(
            """[{"Name":"peer-key"},{"Name":"invite-key"}]""",
            keys,
            [Node("peer-key")]
        );

        Assert.Equal(1, linked);
        Assert.Equal(NodeId, keys[0].ClusterNodeId);
        Assert.Null(keys[1].ClusterNodeId);
    }

    [Fact]
    public void AnExplicitNullIsNotRelinkedAndNeitherIsAnyCasingOfTheProperty()
    {
        List<TsigKeyEntity> keys = [new() { Name = "peer-key" }, new() { Name = "peer-key" }];

        int linked = BackupLegacyFixups.LinkClusterKeys(
            """[{"Name":"peer-key","ClusterNodeId":null},{"Name":"peer-key","clusterNodeId":null}]""",
            keys,
            [Node("peer-key")]
        );

        Assert.Equal(0, linked);
        Assert.All(keys, key => Assert.Null(key.ClusterNodeId));
    }

    [Fact]
    public void MatchingIsOrdinal()
    {
        List<TsigKeyEntity> keys = [new() { Name = "Peer-Key" }];

        int linked = BackupLegacyFixups.LinkClusterKeys(
            """[{"Name":"Peer-Key"}]""",
            keys,
            [Node("peer-key")]
        );

        Assert.Equal(0, linked);
        Assert.Null(keys[0].ClusterNodeId);
    }

    [Fact]
    public void AMismatchedListIsLeftAlone()
    {
        List<TsigKeyEntity> keys = [new() { Name = "peer-key" }];

        Assert.Equal(
            0,
            BackupLegacyFixups.LinkClusterKeys(
                """[{"Name":"a"},{"Name":"b"}]""",
                keys,
                [Node("peer-key")]
            )
        );
        Assert.Equal(0, BackupLegacyFixups.LinkClusterKeys("", keys, [Node("peer-key")]));
    }
}
