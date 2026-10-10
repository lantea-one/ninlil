using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Replication;

/// <summary>
///     This class runs the replica routing tests against the MySQL source and GTID replica. It
///     needs Docker.
/// </summary>
[Collection(MySqlReplicatedCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MySqlReplicaRoutingTests(MySqlReplicatedEngineFixture fixture)
    : ReplicaRoutingTestsBase(fixture.Replicated)
{
    /// <inheritdoc />
    protected override async Task<string> PrepareDatabaseAsync()
    {
        string name = $"routing_{Guid.NewGuid():N}";
        await using EngineDatabase database = await fixture.Replicated.CreateDatabaseAsync(name);
        await fixture.Replicated.WaitForCatchUpAsync(name);
        return name;
    }
}
