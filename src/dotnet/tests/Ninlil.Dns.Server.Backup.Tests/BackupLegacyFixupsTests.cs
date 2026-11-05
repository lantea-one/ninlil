using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Server.Backup.Catalog;

// Define our namespace.
namespace Ninlil.Dns.Server.Backup.Tests;

/// <summary>
///     This class tests <see cref="BackupLegacyFixups.LinkClusterKeys" /> and
///     <see cref="BackupLegacyFixups.LinkLeasesToScopes" />.
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

    private static readonly Guid LanId = Guid.NewGuid();
    private static readonly Guid GuestId = Guid.NewGuid();

    private static List<DhcpScopeEntity> Scopes() =>
        [new() { Id = LanId, Name = "lan" }, new() { Id = GuestId, Name = "guest" }];

    [Fact]
    public void LeasesWithoutAScopeIdAreRelinkedByScopeNameAndOrphansAreDropped()
    {
        // The archive format of a version before the scope id: two scopes that held the same
        // address, and a lease of a scope that no longer exists.
        const string json = """
            [
              {"ScopeName":"lan","ClientIdentifier":"a","IpAddress":"10.0.0.5"},
              {"ScopeName":"guest","ClientIdentifier":"b","IpAddress":"10.0.0.5"},
              {"ScopeName":"deleted","ClientIdentifier":"c","IpAddress":"10.0.0.5"}
            ]
            """;
        List<LeaseEntity> leases =
        [
            new() { ScopeName = "lan", ClientIdentifier = "a" },
            new() { ScopeName = "guest", ClientIdentifier = "b" },
            new() { ScopeName = "deleted", ClientIdentifier = "c" },
        ];

        (int linked, int removed) = BackupLegacyFixups.LinkLeasesToScopes(json, leases, Scopes());

        Assert.Equal((2, 1), (linked, removed));
        Assert.Equal(2, leases.Count);
        Assert.Equal(LanId, leases[0].ScopeId);
        Assert.Equal(GuestId, leases[1].ScopeId);
    }

    [Fact]
    public void LeasesThatCarryAScopeIdPropertyAreNeverChanged()
    {
        Guid kept = Guid.NewGuid();
        List<LeaseEntity> leases =
        [
            new() { ScopeName = "lan", ScopeId = kept },
            new() { ScopeName = "deleted", ScopeId = Guid.Empty },
        ];

        (int linked, int removed) = BackupLegacyFixups.LinkLeasesToScopes(
            """[{"ScopeName":"lan","ScopeId":"%s"},{"ScopeName":"deleted","scopeId":"%s"}]""".Replace(
                "%s",
                kept.ToString()
            ),
            leases,
            Scopes()
        );

        Assert.Equal((0, 0), (linked, removed));
        Assert.Equal(2, leases.Count);
        Assert.Equal(kept, leases[0].ScopeId);
    }

    [Fact]
    public void LeaseScopeNameMatchingIsOrdinal()
    {
        List<LeaseEntity> leases = [new() { ScopeName = "LAN" }];

        (int linked, int removed) = BackupLegacyFixups.LinkLeasesToScopes(
            """[{"ScopeName":"LAN"}]""",
            leases,
            Scopes()
        );

        Assert.Equal((0, 1), (linked, removed));
        Assert.Empty(leases);
    }

    [Fact]
    public void ALeaseListThatDoesNotMatchTheJsonIsLeftAlone()
    {
        List<LeaseEntity> leases = [new() { ScopeName = "deleted" }];

        (int linked, int removed) = BackupLegacyFixups.LinkLeasesToScopes(
            """[{"ScopeName":"deleted"},{"ScopeName":"lan"}]""",
            leases,
            Scopes()
        );

        Assert.Equal((0, 0), (linked, removed));
        Assert.Single(leases);
    }
}
