using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Replication;

/// <summary>
///     This class runs the replica routing tests against the PostgreSQL primary and hot standby.
///     It needs Docker.
/// </summary>
[Collection(PostgreSqlReplicatedCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class PostgreSqlReplicaRoutingTests(PostgreSqlReplicatedEngineFixture fixture)
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
