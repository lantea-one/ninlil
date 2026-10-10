using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Replication;

/// <summary>
///     This class runs the replica health monitor tests against the PostgreSql primary and its replica.
///     It needs Docker.
/// </summary>
[Collection(PostgreSqlReplicatedCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class PostgreSqlReplicaHealthTests(PostgreSqlReplicatedEngineFixture fixture)
    : RelationalReplicaHealthTestsBase(fixture.Replicated);
