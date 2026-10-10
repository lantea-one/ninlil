using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Replication;

/// <summary>
///     This class runs the replica routing tests against the SQL Server read-scale availability
///     group. The secondary only accepts connections that carry <c>ApplicationIntent=ReadOnly</c>,
///     so a passing read proves the replica connection string carries it. It needs Docker.
/// </summary>
[Collection(SqlServerReplicatedCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class SqlServerReplicaRoutingTests(SqlServerReplicatedEngineFixture fixture)
    : ReplicaRoutingTestsBase(fixture.Replicated)
{
    /// <inheritdoc />
    protected override async Task<string> PrepareDatabaseAsync()
    {
        string name = $"routing_{Guid.NewGuid():N}";
        SqlServerReplicatedEngineHost host = (SqlServerReplicatedEngineHost)fixture.Replicated;
        await host.CreateReplicatedDatabaseAsync(name);
        await using EngineDatabase database = await host.CreateDatabaseAsync(name);
        await host.WaitForCatchUpAsync(name);
        return name;
    }
}
