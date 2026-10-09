using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;
using Ninlil.Dns.Data.MongoDb;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Mongo;

/// <summary>
///     This class holds the transaction checks that run against both MongoDB deployments. The
///     two concrete classes (<see cref="MongoTransactionReplicaSetTests" /> and
///     <see cref="MongoTransactionStandaloneTests" />) differ only in the fixture, and so in
///     whether a failed save leaves the first user behind.
/// </summary>
public abstract class MongoTransactionTestsBase(EngineFixtureBase fixture)
{
    /// <summary>
    ///     This method creates a user with a unique name, so tests never collide.
    /// </summary>
    protected static UserEntity NewUser(Guid? id = null)
    {
        string name = $"user-{Guid.NewGuid():N}";
        return new UserEntity
        {
            Id = id ?? Guid.NewGuid(),
            UserName = name,
            NormalizedUserName = name.ToUpperInvariant(),
            Email = $"{name}@example.test",
            NormalizedEmail = $"{name}@example.test".ToUpperInvariant(),
        };
    }

    /// <summary>
    ///     This method resolves services from a fresh scope of a new database, the way one
    ///     request would.
    /// </summary>
    protected async Task<(EngineDatabase Database, AsyncServiceScope Scope)> OpenAsync()
    {
        EngineDatabase database = await fixture.CreateDatabaseAsync();
        return (database, database.Services.CreateAsyncScope());
    }

    /// <summary>
    ///     This method stages user A and then a duplicate of an existing user's id, and returns
    ///     whether the save threw, together with whether A was stored afterwards.
    /// </summary>
    protected async Task<(
        bool Threw,
        bool UserAStored,
        bool TransactionsSupported
    )> SaveAFailingPairAsync()
    {
        (EngineDatabase database, AsyncServiceScope scope) = await OpenAsync();
        await using EngineDatabase _ = database;
        await using AsyncServiceScope __ = scope;

        UserEntity existing = NewUser();
        IUserRepository setup = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        await setup.AddAsync(existing);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

        // A second scope is a second request: its own change tracker, its own pending writes.
        await using AsyncServiceScope request = database.Services.CreateAsyncScope();
        IUserRepository users = request.ServiceProvider.GetRequiredService<IUserRepository>();
        UserEntity userA = NewUser();
        await users.AddAsync(userA);
        await users.AddAsync(NewUser(existing.Id));

        bool threw = false;
        try
        {
            await request.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }
        catch (Exception)
        {
            threw = true;
        }

        await using AsyncServiceScope check = database.Services.CreateAsyncScope();
        UserEntity stored = await check
            .ServiceProvider.GetRequiredService<IUserRepository>()
            .GetByIdAsync(userA.Id);
        bool supported = await database
            .Services.GetRequiredService<MongoTransactionSupport>()
            .DetectAsync();

        return (threw, stored is not null, supported);
    }
}

/// <summary>
///     This class tests the unit of work on a single-node MongoDB replica set: a failing save
///     rolls everything back, a good save lands, and a 50 000-entry filter-list refresh next to
///     a list update succeeds. It needs Docker, so it carries the <c>RequiresDocker</c>
///     category.
/// </summary>
[Collection(MongoDbCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoTransactionReplicaSetTests(MongoDbEngineFixture fixture)
    : MongoTransactionTestsBase(fixture)
{
    [Fact]
    public async Task SaveChangesAsync_WhenALaterWriteFails_LeavesNoEarlierWriteBehind()
    {
        (bool threw, bool userAStored, bool supported) = await SaveAFailingPairAsync();

        Assert.True(supported);
        Assert.True(threw);
        Assert.False(userAStored);
    }

    [Fact]
    public async Task SaveChangesAsync_WithTwoGoodWrites_StoresBoth()
    {
        (EngineDatabase database, AsyncServiceScope scope) = await OpenAsync();
        await using EngineDatabase _ = database;
        await using AsyncServiceScope __ = scope;
        IUserRepository users = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        UserEntity first = NewUser();
        UserEntity second = NewUser();

        await users.AddAsync(first);
        await users.AddAsync(second);
        int written = await scope
            .ServiceProvider.GetRequiredService<IUnitOfWork>()
            .SaveChangesAsync();

        await using AsyncServiceScope check = database.Services.CreateAsyncScope();
        IUserRepository reader = check.ServiceProvider.GetRequiredService<IUserRepository>();
        Assert.Equal(2, written);
        Assert.NotNull(await reader.GetByIdAsync(first.Id));
        Assert.NotNull(await reader.GetByIdAsync(second.Id));
    }

    [Fact]
    public async Task SaveChangesAsync_WithAFilterListRefreshOf50000EntriesAndAListUpdate_Succeeds()
    {
        (EngineDatabase database, AsyncServiceScope scope) = await OpenAsync();
        await using EngineDatabase _ = database;
        await using AsyncServiceScope __ = scope;
        IDnsFilterListRepository lists =
            scope.ServiceProvider.GetRequiredService<IDnsFilterListRepository>();
        IDnsFilterListEntryRepository entries =
            scope.ServiceProvider.GetRequiredService<IDnsFilterListEntryRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        DnsFilterListEntity list = new() { Id = Guid.NewGuid(), Name = "refresh-test" };
        await lists.AddAsync(list);
        await unitOfWork.SaveChangesAsync();

        list.Comments = "refreshed";
        lists.Update(list);
        await entries.ReplaceEntriesAsync(
            list.Id,
            [
                .. Enumerable
                    .Range(0, 50_000)
                    .Select(i => new DnsFilterListReplacementEntry($"host{i}.example.test")),
            ]
        );
        await unitOfWork.SaveChangesAsync();

        await using AsyncServiceScope check = database.Services.CreateAsyncScope();
        Assert.Equal(
            50_000,
            await check
                .ServiceProvider.GetRequiredService<IDnsFilterListEntryRepository>()
                .CountByListIdAsync(list.Id)
        );
        Assert.Equal(
            "refreshed",
            (
                await check
                    .ServiceProvider.GetRequiredService<IDnsFilterListRepository>()
                    .GetByIdAsync(list.Id)
            ).Comments
        );
    }
}

/// <summary>
///     This class tests the unit of work on a standalone MongoDB server, which cannot run
///     transactions: transaction support reads false and a failed save leaves the earlier
///     write in place. That documents the limit the startup Warning names. It needs Docker, so
///     it carries the <c>RequiresDocker</c> category.
/// </summary>
[Collection(MongoDbStandaloneCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoTransactionStandaloneTests(MongoDbStandaloneEngineFixture fixture)
    : MongoTransactionTestsBase(fixture)
{
    [Fact]
    public async Task SaveChangesAsync_WhenALaterWriteFails_KeepsTheEarlierWrite()
    {
        (bool threw, bool userAStored, bool supported) = await SaveAFailingPairAsync();

        Assert.False(supported);
        Assert.True(threw);
        Assert.True(userAStored);
    }
}
