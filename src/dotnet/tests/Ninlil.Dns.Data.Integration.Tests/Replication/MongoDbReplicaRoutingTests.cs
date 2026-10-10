using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Replication;

/// <summary>
///     This class runs the replica routing tests against the three-member MongoDB replica set
///     with the read preference <c>Secondary</c>. The write concern is <c>Acknowledged</c> (w:1)
///     because the tests write while the secondaries are paused, and a majority write would wait
///     for them. It needs Docker.
/// </summary>
[Collection(MongoDbReplicaSetCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoDbReplicaRoutingTests(MongoDbReplicaSetEngineFixture fixture)
    : ReplicaRoutingTestsBase(fixture.Replicated)
{
    /// <inheritdoc />
    protected override Task<string> PrepareDatabaseAsync() =>
        Task.FromResult($"routing_{Guid.NewGuid():N}");

    /// <inheritdoc />
    protected override void ConfigureSplit(Dictionary<string, string> values, string databaseName)
    {
        values["Storage:MongoDb:ReadPreference"] = "Secondary";
        values["Storage:MongoDb:WriteConcern"] = "Acknowledged";
    }
}
