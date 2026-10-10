using MongoDB.Bson;
using MongoDB.Driver;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;
using Ninlil.Dns.Data.MongoDb.Maintenance;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Web;

/// <summary>
///     This class runs the read-your-own-writes tests of the real application against the
///     three-member MongoDB replica set with the read preference <c>Secondary</c>. The write
///     concern is <c>Acknowledged</c> (w:1) because the tests write while the secondaries are
///     paused, and a majority write would wait for them. It needs Docker.
/// </summary>
[Collection(MongoDbReplicaSetCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoDbReplicaWebHostTests(MongoDbReplicaSetEngineFixture fixture)
    : ReplicaWebHostTestsBase(fixture.Replicated)
{
    /// <inheritdoc />
    protected override async Task<string> PrepareDatabaseAsync()
    {
        // The health check wants every known schema step recorded on the secondaries, as the
        // startup guard leaves them.
        string name = $"ryow_{Guid.NewGuid():N}";
        IMongoCollection<BsonDocument> steps = new MongoClient(
            fixture.Replicated.BuildConnectionString(name)
        )
            .GetDatabase(name)
            .GetCollection<BsonDocument>("NinlilDnsSchemaSteps");
        foreach (string step in MongoSchemaSteps.Known)
            await steps.InsertOneAsync(new BsonDocument("_id", step));
        await fixture.Replicated.WaitForCatchUpAsync(name);
        return name;
    }

    /// <inheritdoc />
    protected override void ConfigureStorage(
        Dictionary<string, string> variables,
        string databaseName
    )
    {
        variables["NINLIL_STORAGE_ENGINE"] = "MongoDb";
        variables["NINLIL_STORAGE_CONNECTION_STRING"] = fixture.Replicated.BuildConnectionString(
            databaseName
        );
        variables["NINLIL_STORAGE_MONGODB_READ_PREFERENCE"] = "Secondary";
        variables["NINLIL_STORAGE_MONGODB_WRITE_CONCERN"] = "Acknowledged";
    }
}
