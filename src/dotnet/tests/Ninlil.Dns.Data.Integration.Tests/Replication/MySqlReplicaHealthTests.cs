using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Replication;

/// <summary>
///     This class runs the replica health monitor tests against the MySql primary and its replica.
///     It needs Docker.
/// </summary>
[Collection(MySqlReplicatedCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MySqlReplicaHealthTests(MySqlReplicatedEngineFixture fixture)
    : RelationalReplicaHealthTestsBase(fixture.Replicated);
