using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Ninlil.Dns.Data.Abstractions.Routing;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;
using Ninlil.Dns.Data.MongoDb;
using Ninlil.Dns.Data.MongoDb.Maintenance;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Replication;

/// <summary>
///     This class runs the replica health monitor tests against the three-member MongoDB replica
///     set with the read preference <c>Secondary</c>. MongoDB has no migrations; the "schema" is
///     the set of recorded schema steps, so the tests write the step documents themselves (the
///     startup guard also builds indexes, which wait for paused secondaries). The write concern
///     is <c>Acknowledged</c> (w:1) because the tests write while the secondaries are paused. It
///     needs Docker.
/// </summary>
[Collection(MongoDbReplicaSetCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoDbReplicaHealthTests(MongoDbReplicaSetEngineFixture fixture)
    : ReplicaHealthTestsBase(fixture.Replicated)
{
    /// <inheritdoc />
    protected override void ConfigureSplit(Dictionary<string, string> values, string databaseName)
    {
        values["Storage:MongoDb:ReadPreference"] = "Secondary";
        values["Storage:MongoDb:WriteConcern"] = "Acknowledged";
    }

    /// <inheritdoc />
    protected override bool BehindDatabaseStartsEligible => false;

    /// <summary>
    ///     On a replica set with no writes the secondaries stay current: every check across two
    ///     no-op write cycles of the primary (about 10 seconds apart) finds the replica eligible
    ///     with the product's default 5-second lag limit. The no-op write moves the primary's
    ///     optime ahead of the secondary's reported one for a moment, and an old optime on an
    ///     idle set is not lag.
    /// </summary>
    [Fact]
    public async Task Monitor_OnAnIdleReplicaSet_KeepsTheReplicaEligible()
    {
        string database = await PrepareDatabaseAsync();
        await Task.Delay(TimeSpan.FromSeconds(15));
        await using ServiceProvider services = BuildServices(database, maxLagSeconds: 5);
        IStorageReadRouter router = services.GetRequiredService<IStorageReadRouter>();

        await StartMonitorAsync(services);
        try
        {
            DateTimeOffset? lastChecked = null;
            int checks = 0;
            DateTime end = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (DateTime.UtcNow < end)
            {
                ReplicaHealthStatus status = router.ReplicaHealth;
                if (status.CheckedAtUtc.HasValue && status.CheckedAtUtc != lastChecked)
                {
                    lastChecked = status.CheckedAtUtc;
                    checks++;
                    Assert.True(
                        status.IsEligible,
                        $"Check {checks} found the idle replica not eligible: {status.Reason}"
                    );
                }

                await Task.Delay(100);
            }

            Assert.True(checks >= 5, $"Only {checks} checks ran.");
        }
        finally
        {
            await StopMonitorAsync(services);
        }
    }

    private async Task RecordStepsAsync(string databaseName, IEnumerable<string> steps)
    {
        IMongoCollection<BsonDocument> collection = new MongoClient(
            fixture.Replicated.BuildConnectionString(databaseName)
        )
            .GetDatabase(databaseName)
            .GetCollection<BsonDocument>("NinlilDnsSchemaSteps")
            .WithWriteConcern(WriteConcern.W1);
        foreach (string step in steps)
            await collection.InsertOneAsync(new BsonDocument("_id", step));
    }

    /// <inheritdoc />
    protected override async Task<string> PrepareDatabaseAsync()
    {
        string name = $"health_{Guid.NewGuid():N}";
        await RecordStepsAsync(name, MongoSchemaSteps.Known);
        await fixture.Replicated.WaitForCatchUpAsync(name);
        return name;
    }

    /// <inheritdoc />
    protected override async Task<string> PrepareDatabaseOneStepBehindAsync()
    {
        string name = $"health_{Guid.NewGuid():N}";
        await RecordStepsAsync(name, MongoSchemaSteps.Known.SkipLast(1));
        await fixture.Replicated.WaitForCatchUpAsync(name);
        return name;
    }

    /// <inheritdoc />
    protected override Task ApplyLastSchemaStepOnPrimaryAsync(string databaseName) =>
        RecordStepsAsync(databaseName, [MongoSchemaSteps.Known[^1]]);
}
