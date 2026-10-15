using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;
using Ninlil.Dns.Data.MongoDb;
using Ninlil.Dns.Data.MongoDb.Maintenance;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Mongo;

/// <summary>
///     This class tests <see cref="MongoIndexCatalog" /> (which <see cref="MongoStartupGuard" />
///     runs at startup) against a real MongoDB container: every collection gets the indexes the
///     design lists, a second run changes nothing, the superseded single-field query-log indexes
///     are dropped, duplicate values do not stop an index from being built, and the lookups the
///     host runs per request use an index scan (checked with the server's own <c>explain</c>).
///     It needs Docker, so it carries the <c>RequiresDocker</c> category.
/// </summary>
[Collection(MongoDbCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoIndexCatalogTests(MongoDbEngineFixture fixture)
{
    /// <summary>
    ///     This field lists, per collection, every index the catalog must create. A key reads
    ///     <c>Field:1</c> or <c>Field:-1</c>, fields joined with a comma.
    /// </summary>
    private static readonly Dictionary<string, string[]> ExpectedIndexes = new()
    {
        ["NinlilDnsUsers"] =
        [
            "NormalizedUserName:1",
            "NormalizedEmail:1",
            "ExternalIdentityProvider:1,ExternalSubjectId:1",
        ],
        ["NinlilDnsRoles"] = ["NormalizedName:1"],
        ["NinlilDnsUserRoles"] = ["UserId:1", "RoleId:1"],
        ["NinlilDnsUserClaims"] = ["UserId:1", "ClaimType:1,ClaimValue:1"],
        ["NinlilDnsRoleClaims"] = ["RoleId:1"],
        ["NinlilDnsSessions"] = ["RefreshTokenHash:1", "UserId:1"],
        ["NinlilDnsApiTokens"] = ["TokenHash:1", "UserId:1"],
        ["NinlilDnsLogs"] = ["TimestampUtc:-1"],
        ["NinlilDnsMetrics"] = ["Category:1,Name:1,TimestampUtc:1"],
        ["NinlilDnsZones"] = ["Name:1"],
        ["NinlilDnsRecords"] = ["ZoneId:1,Name:1,Type:1", "ManagedByFeature:1,Type:1"],
        ["NinlilDnsTsigKeys"] = ["Name:1"],
        ["NinlilDnsDnssecKeys"] = ["ZoneId:1"],
        ["NinlilDnsZoneChangeJournalEntries"] = ["ZoneId:1,OldSerial:1"],
        ["NinlilDnsSecondaryZoneRefreshStates"] = ["ZoneId:1"],
        ["NinlilDnsCloudDnsProviderCredentials"] = ["Name:1"],
        ["NinlilDnsFilterLists"] = ["Name:1"],
        ["NinlilDnsFilterListEntries"] = ["ListId:1,Value:1"],
        ["NinlilDnsClientGroups"] = ["Name:1"],
        ["NinlilDnsClients"] = ["ClientGroupId:1", "CreatedAtUtc:1,_id:1"],
        ["NinlilDnsClientAddresses"] =
        [
            "ClientIdentifier:1,ProtocolVersion:1",
            "ClientId:1",
            "LastSeenUtc:1",
        ],
        ["NinlilDnsClientGroupNetworks"] = ["ClientGroupId:1"],
        ["NinlilDnsClientGroupListAssignments"] = ["ClientGroupId:1", "ListId:1"],
        ["NinlilDnsClientListAssignments"] = ["ClientId:1", "ListId:1"],
        ["NinlilDnsClientSchedules"] = ["ClientId:1"],
        ["NinlilDnsClientGroupSchedules"] = ["ClientGroupId:1"],
        ["NinlilDnsQueryLogEntries"] =
        [
            "OccurredAtUtc:-1",
            "ClientAddress:1,OccurredAtUtc:-1",
            "ClientIdentifier:1,OccurredAtUtc:-1",
            "ResponseCode:1,OccurredAtUtc:-1",
        ],
        ["NinlilDnsWebhookDeliveryAttempts"] =
        [
            "Status:1,NextRetryDueUtc:1",
            "CompletedAtUtc:1",
            "SubscriptionId:1",
            "SubscriptionId:1,CreatedAtUtc:-1,_id:-1",
        ],
        ["NinlilDnsWebhookSubscriptions"] = ["Name:1"],
        ["NinlilDhcpScopes"] = ["Name:1"],
        ["NinlilDhcpLeases"] =
        [
            "ScopeName:1,ClientIdentifier:1",
            "ScopeName:1,IpAddress:1",
            "IpAddress:1",
            "State:1,LeaseExpiresAtUtc:1",
        ],
        ["NinlilDhcpReservations"] = ["ScopeId:1,ClientIdentifier:1", "ScopeId:1,IpAddress:1"],
        ["NinlilDhcpScopeCustomOptions"] = ["ScopeId:1,OptionTag:1"],
        ["NinlilDhcpTagRuleOptions"] = ["TagRuleId:1,OptionTag:1"],
        ["NinlilDnsClusterNodes"] = ["RemoteNodeId:1"],
        ["NinlilDnsClusterSyncTombstones"] = ["EntityType:1,EntityKey:1"],
        ["NinlilDnsClusterSyncedSettingsStates"] = ["SectionName:1"],
    };

    private static NinlilDnsMongoContext GetContext(EngineDatabase database) =>
        database.Services.GetRequiredService<NinlilDnsMongoContext>();

    private static string KeyText(BsonDocument key) =>
        string.Join(",", key.Elements.Select(e => $"{e.Name}:{e.Value.ToInt32()}"));

    private static async Task<List<BsonDocument>> ListIndexesAsync(
        NinlilDnsMongoContext context,
        string collection
    ) =>
        await (
            await context.Database.GetCollection<BsonDocument>(collection).Indexes.ListAsync()
        ).ToListAsync();

    private static async Task<List<string>> IndexKeysAsync(
        NinlilDnsMongoContext context,
        string collection
    ) =>
        (await ListIndexesAsync(context, collection))
            .Select(index => KeyText(index["key"].AsBsonDocument))
            .ToList();

    /// <summary>
    ///     This method runs <c>explain</c> on a find and returns the winning plan as text, so a
    ///     test can look for the stage names in it.
    /// </summary>
    private static async Task<string> ExplainFindAsync(
        NinlilDnsMongoContext context,
        string collection,
        BsonDocument filter,
        BsonDocument sort = null
    )
    {
        BsonDocument find = new() { { "find", collection }, { "filter", filter } };
        if (sort != null)
            find["sort"] = sort;

        BsonDocument result = await context.Database.RunCommandAsync<BsonDocument>(
            new BsonDocument { { "explain", find }, { "verbosity", "queryPlanner" } }
        );
        return result["queryPlanner"]["winningPlan"].ToJson();
    }

    private static BsonBinaryData Guid4(Guid id) => new(id, GuidRepresentation.Standard);

    [Fact]
    public async Task EnsureAsync_OnAFreshDatabase_CreatesEveryListedIndex()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        NinlilDnsMongoContext context = GetContext(database);

        await MongoIndexCatalog.EnsureAsync(context, NullLogger.Instance);

        foreach ((string collection, string[] expected) in ExpectedIndexes)
        {
            List<string> actual = await IndexKeysAsync(context, collection);
            foreach (string key in expected)
                Assert.True(actual.Contains(key), $"{collection} lacks the index {key}.");

            // The _id index plus exactly the listed ones: nothing else is created.
            Assert.Equal(expected.Length + 1, actual.Count);
        }
    }

    [Fact]
    public async Task EnsureAsync_RunTwice_CreatesNothingNewAndDoesNotFail()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        NinlilDnsMongoContext context = GetContext(database);

        await MongoIndexCatalog.EnsureAsync(context, NullLogger.Instance);
        List<string> first = (await ListIndexesAsync(context, "NinlilDnsRecords"))
            .Select(index => index["name"].AsString)
            .Order()
            .ToList();
        await MongoIndexCatalog.EnsureAsync(context, NullLogger.Instance);
        List<string> second = (await ListIndexesAsync(context, "NinlilDnsRecords"))
            .Select(index => index["name"].AsString)
            .Order()
            .ToList();

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task EnsureAsync_WithTheOldSingleFieldQueryLogIndexes_DropsThem()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        NinlilDnsMongoContext context = GetContext(database);
        IMongoCollection<BsonDocument> queryLog = context.Database.GetCollection<BsonDocument>(
            "NinlilDnsQueryLogEntries"
        );
        foreach (
            string field in new[]
            {
                "QueriedName",
                "ClientAddress",
                "ClientIdentifier",
                "ResponseCode",
            }
        )
            await queryLog.Indexes.CreateOneAsync(
                new CreateIndexModel<BsonDocument>(new BsonDocument(field, 1))
            );

        await MongoIndexCatalog.EnsureAsync(context, NullLogger.Instance);

        Assert.Equal(
            ExpectedIndexes["NinlilDnsQueryLogEntries"].Order(),
            (await IndexKeysAsync(context, "NinlilDnsQueryLogEntries"))
                .Where(key => key != "_id:1")
                .Order()
        );
    }

    [Fact]
    public async Task EnsureAsync_WithDuplicateValuesAlreadyStored_StillBuildsTheIndex()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        NinlilDnsMongoContext context = GetContext(database);
        IMongoCollection<BsonDocument> tokens = context.Database.GetCollection<BsonDocument>(
            "NinlilDnsApiTokens"
        );
        await tokens.InsertManyAsync([
            new BsonDocument { { "_id", Guid4(Guid.NewGuid()) }, { "TokenHash", "same" } },
            new BsonDocument { { "_id", Guid4(Guid.NewGuid()) }, { "TokenHash", "same" } },
        ]);

        await MongoIndexCatalog.EnsureAsync(context, NullLogger.Instance);

        Assert.Contains("TokenHash:1", await IndexKeysAsync(context, "NinlilDnsApiTokens"));
    }

    [Fact]
    public async Task StartupGuard_RunAsync_EnsuresTheIndexes()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        NinlilDnsMongoContext context = GetContext(database);

        await MongoStartupGuard.RunAsync(context, NullLogger.Instance);

        Assert.Contains("TokenHash:1", await IndexKeysAsync(context, "NinlilDnsApiTokens"));
    }

    [Fact]
    public async Task Lookups_AfterEnsureAsync_UseAnIndexScanNotACollectionScan()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        NinlilDnsMongoContext context = GetContext(database);
        await MongoIndexCatalog.EnsureAsync(context, NullLogger.Instance);

        // Some rows, so the planner has something to choose over.
        await context
            .Database.GetCollection<BsonDocument>("NinlilDnsRecords")
            .InsertManyAsync(
                Enumerable
                    .Range(0, 50)
                    .Select(i => new BsonDocument
                    {
                        { "_id", Guid4(Guid.NewGuid()) },
                        { "ZoneId", Guid4(Guid.NewGuid()) },
                        { "Name", $"host{i}" },
                        { "Type", 1 },
                    })
            );

        (string Collection, BsonDocument Filter, BsonDocument Sort)[] lookups =
        [
            (
                "NinlilDnsRecords",
                new BsonDocument { { "ZoneId", Guid4(Guid.NewGuid()) }, { "Name", "www" } },
                null
            ),
            (
                "NinlilDhcpLeases",
                new BsonDocument { { "ScopeName", "lan" }, { "ClientIdentifier", "aa:bb" } },
                null
            ),
            ("NinlilDnsApiTokens", new BsonDocument("TokenHash", "abc"), null),
            (
                "NinlilDnsQueryLogEntries",
                new BsonDocument("ClientAddress", "192.0.2.1"),
                new BsonDocument("OccurredAtUtc", -1)
            ),
        ];

        foreach ((string collection, BsonDocument filter, BsonDocument sort) in lookups)
        {
            string plan = await ExplainFindAsync(context, collection, filter, sort);
            Assert.Contains("IXSCAN", plan);
            Assert.DoesNotContain("COLLSCAN", plan);
        }

        // A subscription's attempts page (newest first, ties broken on the id) is read straight
        // off the index, with no blocking sort stage.
        string pagePlan = await ExplainFindAsync(
            context,
            "NinlilDnsWebhookDeliveryAttempts",
            new BsonDocument("SubscriptionId", Guid4(Guid.NewGuid())),
            new BsonDocument { { "CreatedAtUtc", -1 }, { "_id", -1 } }
        );
        Assert.Contains("IXSCAN", pagePlan);
        Assert.DoesNotContain("SORT", pagePlan);
    }
}
