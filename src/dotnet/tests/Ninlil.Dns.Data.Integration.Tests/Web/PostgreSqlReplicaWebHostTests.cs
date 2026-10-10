using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Web;

/// <summary>
///     This class runs the read-your-own-writes tests of the real application against the
///     PostgreSQL primary and hot standby. It needs Docker.
/// </summary>
[Collection(PostgreSqlReplicatedCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class PostgreSqlReplicaWebHostTests(PostgreSqlReplicatedEngineFixture fixture)
    : ReplicaWebHostTestsBase(fixture.Replicated)
{
    /// <inheritdoc />
    protected override async Task<string> PrepareDatabaseAsync()
    {
        // An empty database: the application applies the migrations itself at startup, on the
        // primary, and the standby follows.
        string name = $"ryow_{Guid.NewGuid():N}";
        await using EngineDatabase database = await fixture.Replicated.CreateDatabaseAsync(
            name,
            createSchema: false
        );
        await fixture.Replicated.WaitForCatchUpAsync(name);
        return name;
    }

    /// <inheritdoc />
    protected override void ConfigureStorage(
        Dictionary<string, string> variables,
        string databaseName
    )
    {
        variables["NINLIL_STORAGE_ENGINE"] = "PostgreSql";
        variables["NINLIL_STORAGE_CONNECTION_STRING"] = fixture.Replicated.BuildConnectionString(
            databaseName
        );
        variables["NINLIL_STORAGE_READ_REPLICA_CONNECTION_STRING"] =
            fixture.Replicated.BuildReplicaConnectionString(databaseName);
    }
}
