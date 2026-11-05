using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Exceptions;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;
using Ninlil.Dns.Data.MongoDb;
using Ninlil.Dns.Data.MongoDb.Maintenance;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Mongo;

/// <summary>
///     This class tests that MongoDB enforces what the relational engines enforce with their
///     unique indexes: one DHCP custom option per tag on a scope, and one option per tag on a tag
///     rule. It runs against a real MongoDB container (the replica-set fixture) and covers three
///     things. A second insert with the same tag throws the <see cref="ConflictException" /> the
///     controllers answer with 409. A database that already holds duplicates refuses the start
///     (<see cref="DatabaseStartupRefusedException" />), lists them and deletes nothing. After the
///     operator deletes the extra documents the start succeeds, the unique index exists and
///     the step is recorded. The duplicates are written straight into the collection, bypassing
///     the repositories. It needs Docker, so it carries the <c>RequiresDocker</c> category.
/// </summary>
[Collection(MongoDbCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoDhcpCustomOptionUniquenessTests(MongoDbEngineFixture fixture)
{
    private const string ScopeOptions = "NinlilDhcpScopeCustomOptions";
    private const string TagRuleOptions = "NinlilDhcpTagRuleOptions";
    private const string StepName = "DhcpCustomOptionUniqueIndexes";

    private static NinlilDnsMongoContext GetContext(EngineDatabase database) =>
        database.Services.GetRequiredService<NinlilDnsMongoContext>();

    private static BsonBinaryData Guid4(Guid id) => new(id, GuidRepresentation.Standard);

    private static BsonDocument OptionDocument(string ownerField, Guid owner, int tag, Guid id) =>
        new()
        {
            { "_id", Guid4(id) },
            { ownerField, Guid4(owner) },
            { "OptionTag", tag },
            { "ValueHex", "0A" },
        };

    private static async Task SaveScopeOptionAsync(EngineDatabase database, Guid scopeId, byte tag)
    {
        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<IDhcpScopeCustomOptionRepository>()
            .AddAsync(
                new DhcpScopeCustomOptionEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ScopeId = scopeId,
                    OptionTag = tag,
                    ValueHex = "0A",
                }
            );
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    private static async Task SaveTagRuleOptionAsync(
        EngineDatabase database,
        Guid tagRuleId,
        byte tag
    )
    {
        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<IDhcpTagRuleOptionRepository>()
            .AddAsync(
                new DhcpTagRuleOptionEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    TagRuleId = tagRuleId,
                    OptionTag = tag,
                    ValueHex = "0A",
                }
            );
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    private static async Task<bool> HasUniqueIndexAsync(
        NinlilDnsMongoContext context,
        string collection,
        string key
    )
    {
        List<BsonDocument> indexes = await (
            await context.Database.GetCollection<BsonDocument>(collection).Indexes.ListAsync()
        ).ToListAsync();
        return indexes.Any(index =>
            index["key"].AsBsonDocument.Elements.Select(e => e.Name).SequenceEqual(key.Split(','))
            && index.GetValue("unique", false).ToBoolean()
        );
    }

    /// <summary>
    ///     A second custom option with the same tag on one scope throws the conflict the
    ///     controller maps to 409, and a different tag or scope is still accepted.
    /// </summary>
    [Fact]
    public async Task SaveChanges_WithASecondScopeOptionOfTheSameTag_ThrowsAConflict()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        await MongoStartupGuard.RunAsync(GetContext(database), NullLogger.Instance);
        Guid scopeId = Guid.NewGuid();

        await SaveScopeOptionAsync(database, scopeId, 42);

        await Assert.ThrowsAsync<ConflictException>(() =>
            SaveScopeOptionAsync(database, scopeId, 42)
        );
        await SaveScopeOptionAsync(database, scopeId, 43);
        await SaveScopeOptionAsync(database, Guid.NewGuid(), 42);
    }

    /// <summary>
    ///     A second option with the same tag on one tag rule throws the same conflict.
    /// </summary>
    [Fact]
    public async Task SaveChanges_WithASecondTagRuleOptionOfTheSameTag_ThrowsAConflict()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        await MongoStartupGuard.RunAsync(GetContext(database), NullLogger.Instance);
        Guid tagRuleId = Guid.NewGuid();

        await SaveTagRuleOptionAsync(database, tagRuleId, 42);

        await Assert.ThrowsAsync<ConflictException>(() =>
            SaveTagRuleOptionAsync(database, tagRuleId, 42)
        );
        await SaveTagRuleOptionAsync(database, tagRuleId, 43);
        await SaveTagRuleOptionAsync(database, Guid.NewGuid(), 42);
    }

    /// <summary>
    ///     Existing duplicates refuse the start with every duplicate listed (collection, owner,
    ///     tag and document ids), delete nothing and record no step. After the extra documents
    ///     are deleted the next start succeeds and both unique indexes exist.
    /// </summary>
    [Fact]
    public async Task RunAsync_WithExistingDuplicates_RefusesListsThemAndStartsAfterTheyAreDeleted()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        NinlilDnsMongoContext context = GetContext(database);
        IMongoCollection<BsonDocument> scopeOptions = context.Database.GetCollection<BsonDocument>(
            ScopeOptions
        );
        IMongoCollection<BsonDocument> tagRuleOptions =
            context.Database.GetCollection<BsonDocument>(TagRuleOptions);
        Guid scopeId = Guid.NewGuid();
        Guid tagRuleId = Guid.NewGuid();
        Guid scopeFirst = Guid.NewGuid();
        Guid scopeSecond = Guid.NewGuid();
        Guid ruleFirst = Guid.NewGuid();
        Guid ruleSecond = Guid.NewGuid();
        await scopeOptions.InsertManyAsync([
            OptionDocument("ScopeId", scopeId, 66, scopeFirst),
            OptionDocument("ScopeId", scopeId, 66, scopeSecond),
            OptionDocument("ScopeId", scopeId, 67, Guid.NewGuid()),
        ]);
        await tagRuleOptions.InsertManyAsync([
            OptionDocument("TagRuleId", tagRuleId, 150, ruleFirst),
            OptionDocument("TagRuleId", tagRuleId, 150, ruleSecond),
        ]);

        DatabaseStartupRefusedException refusal =
            await Assert.ThrowsAsync<DatabaseStartupRefusedException>(() =>
                MongoStartupGuard.RunAsync(context, NullLogger.Instance)
            );

        foreach (
            string expected in new[]
            {
                ScopeOptions,
                TagRuleOptions,
                scopeId.ToString(),
                tagRuleId.ToString(),
                scopeFirst.ToString(),
                scopeSecond.ToString(),
                ruleFirst.ToString(),
                ruleSecond.ToString(),
                "66",
                "150",
                "delete",
            }
        )
            Assert.Contains(expected, refusal.Message);
        Assert.Equal(3, await scopeOptions.CountDocumentsAsync(_ => true));
        Assert.Equal(2, await tagRuleOptions.CountDocumentsAsync(_ => true));
        Assert.Equal(
            0,
            await context.SchemaSteps.CountDocumentsAsync(
                Builders<BsonDocument>.Filter.Eq("_id", StepName)
            )
        );

        await scopeOptions.DeleteOneAsync(new BsonDocument("_id", Guid4(scopeSecond)));
        await tagRuleOptions.DeleteOneAsync(new BsonDocument("_id", Guid4(ruleSecond)));

        await MongoStartupGuard.RunAsync(context, NullLogger.Instance);

        Assert.True(await HasUniqueIndexAsync(context, ScopeOptions, "ScopeId,OptionTag"));
        Assert.True(await HasUniqueIndexAsync(context, TagRuleOptions, "TagRuleId,OptionTag"));
        Assert.Equal(
            MongoSchemaSteps.Known.Count,
            await context.SchemaSteps.CountDocumentsAsync(_ => true)
        );
    }

    /// <summary>
    ///     A database with the old non-unique compound indexes (an earlier build) gets them
    ///     replaced by unique ones, and a second start changes nothing.
    /// </summary>
    [Fact]
    public async Task RunAsync_WithTheOldPlainIndexes_ReplacesThemWithUniqueOnesIdempotently()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        NinlilDnsMongoContext context = GetContext(database);
        await context
            .Database.GetCollection<BsonDocument>(ScopeOptions)
            .Indexes.CreateOneAsync(
                new CreateIndexModel<BsonDocument>(
                    new BsonDocument { { "ScopeId", 1 }, { "OptionTag", 1 } }
                )
            );
        await context
            .Database.GetCollection<BsonDocument>(TagRuleOptions)
            .Indexes.CreateOneAsync(
                new CreateIndexModel<BsonDocument>(
                    new BsonDocument { { "TagRuleId", 1 }, { "OptionTag", 1 } }
                )
            );

        await MongoStartupGuard.RunAsync(context, NullLogger.Instance);
        await MongoStartupGuard.RunAsync(context, NullLogger.Instance);

        Assert.True(await HasUniqueIndexAsync(context, ScopeOptions, "ScopeId,OptionTag"));
        Assert.True(await HasUniqueIndexAsync(context, TagRuleOptions, "TagRuleId,OptionTag"));
    }
}
