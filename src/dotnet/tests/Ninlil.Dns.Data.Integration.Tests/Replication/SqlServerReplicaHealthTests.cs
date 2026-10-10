using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Replication;

/// <summary>
///     This class runs the replica health monitor tests against the SQL Server read-scale
///     availability group. It needs Docker.
/// </summary>
[Collection(SqlServerReplicatedCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class SqlServerReplicaHealthTests(SqlServerReplicatedEngineFixture fixture)
    : RelationalReplicaHealthTestsBase(fixture.Replicated)
{
    /// <inheritdoc />
    protected override Task CreateReplicatedDatabaseAsync(string name) =>
        ((SqlServerReplicatedEngineHost)fixture.Replicated).CreateReplicatedDatabaseAsync(name);
}
