using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;
using Ninlil.Dns.Data.MongoDb;
using Ninlil.Dns.Data.MongoDb.Maintenance;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Repository;

/// <summary>
///     This class holds the tests of <see cref="ISecondaryZoneRefreshStateRepository" /> that run
///     against every engine, one subclass per engine. They cover the round trip of the refresh
///     time (to the millisecond), update, lookup of an unknown zone, the unique index on the zone
///     identifier and listing all rows. Each test uses a fresh database.
/// </summary>
public abstract class SecondaryZoneRefreshStateTestsBase(EngineFixtureBase fixture)
{
    /// <summary>
    ///     A refresh time with a millisecond part, so the tests fail on an engine that rounds
    ///     to the second.
    /// </summary>
    private static readonly DateTimeOffset RefreshedAt = new(
        2026,
        10,
        15,
        8,
        30,
        15,
        123,
        TimeSpan.Zero
    );

    /// <summary>
    ///     A row saved for a zone is found by that zone with the same identifier and a refresh
    ///     time equal to the millisecond.
    /// </summary>
    [Fact]
    public async Task FindByZoneIdAsync_AfterAdd_ReturnsTheRowWithTheRefreshTimeToTheMillisecond()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        Guid zoneId = Guid.NewGuid();
        SecondaryZoneRefreshStateEntity added = NewState(zoneId, RefreshedAt);

        await using (AsyncServiceScope seed = database.Services.CreateAsyncScope())
        {
            await Repository(seed).AddAsync(added);
            await Save(seed);
        }

        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
        SecondaryZoneRefreshStateEntity found = await Repository(scope).FindByZoneIdAsync(zoneId);

        Assert.NotNull(found);
        Assert.Equal(added.Id, found.Id);
        Assert.Equal(zoneId, found.ZoneId);
        Assert.Equal(
            RefreshedAt.ToUnixTimeMilliseconds(),
            found.LastRefreshedAtUtc.ToUnixTimeMilliseconds()
        );
    }

    /// <summary>
    ///     A changed refresh time that is saved reads back as the new value.
    /// </summary>
    [Fact]
    public async Task Update_ANewRefreshTime_ReadsBackAsTheNewTime()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        Guid zoneId = Guid.NewGuid();
        DateTimeOffset later = RefreshedAt.AddHours(5).AddMilliseconds(7);

        await using (AsyncServiceScope seed = database.Services.CreateAsyncScope())
        {
            await Repository(seed).AddAsync(NewState(zoneId, RefreshedAt));
            await Save(seed);
        }

        await using (AsyncServiceScope update = database.Services.CreateAsyncScope())
        {
            ISecondaryZoneRefreshStateRepository repository = Repository(update);
            SecondaryZoneRefreshStateEntity row = await repository.FindByZoneIdAsync(zoneId);
            row.LastRefreshedAtUtc = later;
            repository.Update(row);
            await Save(update);
        }

        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
        SecondaryZoneRefreshStateEntity found = await Repository(scope).FindByZoneIdAsync(zoneId);

        Assert.Equal(
            later.ToUnixTimeMilliseconds(),
            found.LastRefreshedAtUtc.ToUnixTimeMilliseconds()
        );
    }

    /// <summary>
    ///     A zone without a row gives null, not an exception.
    /// </summary>
    [Fact]
    public async Task FindByZoneIdAsync_ForAnUnknownZone_ReturnsNull()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();

        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();

        Assert.Null(await Repository(scope).FindByZoneIdAsync(Guid.NewGuid()));
    }

    /// <summary>
    ///     A second row for the same zone fails when saved, on every engine.
    /// </summary>
    [Fact]
    public async Task SaveChangesAsync_ASecondRowForTheSameZone_Fails()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        await EnsureIndexesAsync(database);
        Guid zoneId = Guid.NewGuid();

        await using (AsyncServiceScope seed = database.Services.CreateAsyncScope())
        {
            await Repository(seed).AddAsync(NewState(zoneId, RefreshedAt));
            await Save(seed);
        }

        await using AsyncServiceScope duplicate = database.Services.CreateAsyncScope();
        await Repository(duplicate).AddAsync(NewState(zoneId, RefreshedAt.AddMinutes(1)));

        await Assert.ThrowsAnyAsync<Exception>(() => Save(duplicate));
    }

    /// <summary>
    ///     Listing returns every row, which is how the refresh service loads its state at start.
    /// </summary>
    [Fact]
    public async Task ListAsync_WithSeveralRows_ReturnsAllOfThem()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        Guid third = Guid.NewGuid();

        await using (AsyncServiceScope seed = database.Services.CreateAsyncScope())
        {
            ISecondaryZoneRefreshStateRepository repository = Repository(seed);
            await repository.AddAsync(NewState(first, RefreshedAt));
            await repository.AddAsync(NewState(second, RefreshedAt.AddMinutes(1)));
            await repository.AddAsync(NewState(third, RefreshedAt.AddMinutes(2)));
            await Save(seed);
        }

        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
        IReadOnlyList<SecondaryZoneRefreshStateEntity> rows = await Repository(scope).ListAsync();

        Assert.Equal(
            new[] { first, second, third }.Order(),
            rows.Select(row => row.ZoneId).Order()
        );
    }

    /// <summary>
    ///     This method creates the MongoDB indexes when the database is a MongoDB one. The
    ///     unique index normally comes from the startup guard, which the test databases skip.
    ///     Relational databases get theirs from the migration, so nothing happens for them.
    /// </summary>
    /// <param name="database">The database under test.</param>
    private static async Task EnsureIndexesAsync(EngineDatabase database)
    {
        NinlilDnsMongoContext context = database.Services.GetService<NinlilDnsMongoContext>();
        if (context != null)
            await MongoIndexCatalog.EnsureAsync(context, NullLogger.Instance);
    }

    private static ISecondaryZoneRefreshStateRepository Repository(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<ISecondaryZoneRefreshStateRepository>();

    private static Task Save(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

    private static SecondaryZoneRefreshStateEntity NewState(Guid zoneId, DateTimeOffset at) =>
        new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ZoneId = zoneId,
            LastRefreshedAtUtc = at,
        };
}

/// <summary>
///     This class runs <see cref="SecondaryZoneRefreshStateTestsBase" /> on SQLite (no Docker).
/// </summary>
[Collection(SqliteCollection.Name)]
public sealed class SqliteSecondaryZoneRefreshStateTests(SqliteEngineFixture fixture)
    : SecondaryZoneRefreshStateTestsBase(fixture);

/// <summary>
///     This class runs <see cref="SecondaryZoneRefreshStateTestsBase" /> on PostgreSQL (Docker).
/// </summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class PostgreSqlSecondaryZoneRefreshStateTests(PostgreSqlEngineFixture fixture)
    : SecondaryZoneRefreshStateTestsBase(fixture);

/// <summary>
///     This class runs <see cref="SecondaryZoneRefreshStateTestsBase" /> on MySQL (Docker).
/// </summary>
[Collection(MySqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MySqlSecondaryZoneRefreshStateTests(MySqlEngineFixture fixture)
    : SecondaryZoneRefreshStateTestsBase(fixture);

/// <summary>
///     This class runs <see cref="SecondaryZoneRefreshStateTestsBase" /> on SQL Server (Docker).
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class SqlServerSecondaryZoneRefreshStateTests(SqlServerEngineFixture fixture)
    : SecondaryZoneRefreshStateTestsBase(fixture);

/// <summary>
///     This class runs <see cref="SecondaryZoneRefreshStateTestsBase" /> on a MongoDB replica set
///     (Docker).
/// </summary>
[Collection(MongoDbCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoDbSecondaryZoneRefreshStateTests(MongoDbEngineFixture fixture)
    : SecondaryZoneRefreshStateTestsBase(fixture);

/// <summary>
///     This class runs <see cref="SecondaryZoneRefreshStateTestsBase" /> on a standalone MongoDB
///     (Docker).
/// </summary>
[Collection(MongoDbStandaloneCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoDbStandaloneSecondaryZoneRefreshStateTests(
    MongoDbStandaloneEngineFixture fixture
) : SecondaryZoneRefreshStateTestsBase(fixture);
