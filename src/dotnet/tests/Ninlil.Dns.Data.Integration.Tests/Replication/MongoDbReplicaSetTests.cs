using MongoDB.Bson;
using MongoDB.Driver;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Replication;

/// <summary>
///     This class is the smoke test of the three-member MongoDB replica set fixture, with real
///     containers and a replica-set client: a document written through the primary URL is read
///     back through the secondary-preferring URL, is not visible there while oplog apply is
///     stopped, and appears after resume. A direct connection to a secondary refuses a write
///     ("not primary"). It needs Docker.
/// </summary>
[Collection(MongoDbReplicaSetCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoDbReplicaSetTests(MongoDbReplicaSetEngineFixture fixture)
{
    private const string DatabaseName = "ninlil_smoke";

    private static IMongoCollection<BsonDocument> Collection(string url, string name) =>
        new MongoClient(url).GetDatabase(DatabaseName).GetCollection<BsonDocument>(name);

    private static Task<long> CountAsync(string url, string name) =>
        Collection(url, name).CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);

    [Fact]
    public async Task Secondary_AfterAWriteOnThePrimary_ShowsTheDocument()
    {
        string name = $"smoke_{Guid.NewGuid():N}";
        await Collection(fixture.Replicated.BuildConnectionString(DatabaseName), name)
            .InsertOneAsync(new BsonDocument("a", 1));
        await fixture.Replicated.WaitForCatchUpAsync();

        Assert.Equal(
            1,
            await CountAsync(fixture.Replicated.BuildReplicaConnectionString(DatabaseName), name)
        );
    }

    [Fact]
    public async Task Secondary_WhileApplyIsStopped_DoesNotShowTheDocumentUntilResumed()
    {
        string name = $"smoke_{Guid.NewGuid():N}";
        string primaryUrl = fixture.Replicated.BuildConnectionString(DatabaseName);
        string replicaUrl = fixture.Replicated.BuildReplicaConnectionString(DatabaseName);
        await Collection(primaryUrl, name).InsertOneAsync(new BsonDocument("a", 0));
        await fixture.Replicated.WaitForCatchUpAsync();
        await fixture.Replicated.PauseReplicationAsync();
        try
        {
            // w:1, because a majority write would wait for a secondary that is not applying.
            await Collection(primaryUrl, name)
                .WithWriteConcern(WriteConcern.W1)
                .InsertOneAsync(new BsonDocument("a", 1));
            await Task.Delay(1000);
            Assert.Equal(1, await CountAsync(replicaUrl, name));
        }
        finally
        {
            await fixture.Replicated.ResumeReplicationAsync();
        }

        await fixture.Replicated.WaitForCatchUpAsync();
        Assert.Equal(2, await CountAsync(replicaUrl, name));
    }

    [Fact]
    public async Task Secondary_OnADirectWrite_RefusesIt()
    {
        string url =
            $"mongodb://localhost:{((MongoDbReplicaSetEngineHost)fixture.Replicated).Ports[1]}/?directConnection=true";

        await Assert.ThrowsAnyAsync<MongoException>(async () =>
            await Collection(url, "refused").InsertOneAsync(new BsonDocument("a", 1))
        );
    }
}
