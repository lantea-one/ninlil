using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Ninlil.Dns.Data.Abstractions.Exceptions;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;
using Ninlil.Dns.Data.MongoDb;
using Ninlil.Dns.Data.MongoDb.Maintenance;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Mongo;

/// <summary>
///     This class tests <see cref="MongoStartupGuard" /> against a real MongoDB container (the
///     replica-set fixture): a fresh database records every known schema step, a step this build
///     does not know refuses the start before any backfill runs, a failing backfill is reported
///     as a retryable refusal, and a second start adds no duplicate step documents. It needs
///     Docker, so it carries the <c>RequiresDocker</c> category.
/// </summary>
[Collection(MongoDbCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoStartupGuardTests(MongoDbEngineFixture fixture)
{
    private static NinlilDnsMongoContext GetContext(EngineDatabase database) =>
        database.Services.GetRequiredService<NinlilDnsMongoContext>();

    private static Task InsertLegacyClientAsync(NinlilDnsMongoContext context) =>
        context
            .Database.GetCollection<BsonDocument>("NinlilDnsClients")
            .InsertOneAsync(
                new BsonDocument
                {
                    { "_id", new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard) },
                    { "ClientIdentifier", "legacy-device" },
                    { "ProtocolVersion", BsonNull.Value },
                }
            );

    private static Task<long> CountLegacyClientsAsync(NinlilDnsMongoContext context) =>
        context
            .Database.GetCollection<BsonDocument>("NinlilDnsClients")
            .CountDocumentsAsync(Builders<BsonDocument>.Filter.Exists("ClientIdentifier"));

    [Fact]
    public async Task RunAsync_OnAFreshDatabase_RecordsEveryKnownStep()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        NinlilDnsMongoContext context = GetContext(database);

        await MongoStartupGuard.RunAsync(context, NullLogger.Instance);

        List<BsonDocument> steps = await context.SchemaSteps.Find(_ => true).ToListAsync();
        Assert.Equal(
            MongoSchemaSteps.Known.Order(),
            steps.Select(step => step["_id"].AsString).Order()
        );
        Assert.All(steps, step => Assert.True(step.Contains("appliedAtUtc")));
        Assert.All(
            steps,
            step => Assert.False(string.IsNullOrEmpty(step["applicationVersion"].AsString))
        );
    }

    /// <summary>
    ///     A database that already holds the <c>ZoneJournalSoaHistory</c> step (a start of this
    ///     version) starts again: the step is known, and it has no backfill to run.
    /// </summary>
    [Fact]
    public async Task RunAsync_WithTheZoneJournalSoaHistoryStepStored_StartsAndKeepsTheStep()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        NinlilDnsMongoContext context = GetContext(database);
        await context.SchemaSteps.InsertOneAsync(
            new BsonDocument("_id", MongoSchemaSteps.ZoneJournalSoaHistory)
        );

        await MongoStartupGuard.RunAsync(context, NullLogger.Instance);

        Assert.Equal(
            1,
            await context.SchemaSteps.CountDocumentsAsync(
                Builders<BsonDocument>.Filter.Eq("_id", MongoSchemaSteps.ZoneJournalSoaHistory)
            )
        );
    }

    [Fact]
    public async Task RunAsync_WithAnUnknownStoredStep_RefusesAndRunsNoBackfill()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        NinlilDnsMongoContext context = GetContext(database);
        await context.SchemaSteps.InsertOneAsync(new BsonDocument("_id", "FromTheFuture"));
        await InsertLegacyClientAsync(context);

        DatabaseStartupRefusedException refusal =
            await Assert.ThrowsAsync<DatabaseStartupRefusedException>(() =>
                MongoStartupGuard.RunAsync(context, NullLogger.Instance)
            );

        Assert.Contains("FromTheFuture", refusal.Message);
        Assert.Equal(1, await CountLegacyClientsAsync(context));
        Assert.Equal(1, await context.SchemaSteps.CountDocumentsAsync(_ => true));
    }

    [Fact]
    public async Task RunAsync_WhenABackfillFails_RefusesAndSaysItIsSafeToRetry()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        NinlilDnsMongoContext context = GetContext(database);

        // A validator that rejects every document makes the backfill's address upsert fail.
        await context.Database.CreateCollectionAsync(
            "NinlilDnsClientAddresses",
            new CreateCollectionOptions<BsonDocument>
            {
                Validator = new BsonDocumentFilterDefinition<BsonDocument>(
                    new BsonDocument("neverPresentField", new BsonDocument("$exists", true))
                ),
                ValidationAction = DocumentValidationAction.Error,
                ValidationLevel = DocumentValidationLevel.Strict,
            }
        );
        await InsertLegacyClientAsync(context);

        DatabaseStartupRefusedException refusal =
            await Assert.ThrowsAsync<DatabaseStartupRefusedException>(() =>
                MongoStartupGuard.RunAsync(context, NullLogger.Instance)
            );

        Assert.Contains(MongoSchemaSteps.DnsClientAddressLegacyBackfill, refusal.Message);
        Assert.Contains("safe to retry", refusal.Message);
        Assert.NotNull(refusal.InnerException);
        Assert.Equal(0, await context.SchemaSteps.CountDocumentsAsync(_ => true));
    }

    [Fact]
    public async Task RunAsync_RunTwice_KeepsOneDocumentPerStepAndConvertsLegacyData()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        NinlilDnsMongoContext context = GetContext(database);
        await InsertLegacyClientAsync(context);

        await MongoStartupGuard.RunAsync(context, NullLogger.Instance);
        List<BsonDocument> first = await context.SchemaSteps.Find(_ => true).ToListAsync();
        await MongoStartupGuard.RunAsync(context, NullLogger.Instance);
        List<BsonDocument> second = await context.SchemaSteps.Find(_ => true).ToListAsync();

        Assert.Equal(MongoSchemaSteps.Known.Count, second.Count);
        Assert.Equal(0, await CountLegacyClientsAsync(context));
        Assert.Equal(
            first.OrderBy(step => step["_id"].AsString).Select(step => step["appliedAtUtc"]),
            second.OrderBy(step => step["_id"].AsString).Select(step => step["appliedAtUtc"])
        );
    }
}
