using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Repository;

/// <summary>
///     This class holds the tests of <see cref="IEntityChangeNotifier" /> as the storage engines
///     raise it: a committed save reports the entity types it wrote, once; a failed save, and a
///     save in a rolled-back transaction, report nothing; subscribers for other types are not
///     called; a throwing subscriber breaks neither the other subscribers nor the save. One
///     subclass per engine supplies the fixture (real databases, the engine's real DI
///     registration).
/// </summary>
public abstract class EntityChangeNotificationTestsBase(EngineFixtureBase fixture)
{
    private static ZoneEntity NewZone(string name) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Type = ZoneTypeEnum.Primary,
        };

    private static RecordEntity NewRecord(Guid zoneId) =>
        new()
        {
            Id = Guid.NewGuid(),
            ZoneId = zoneId,
            Name = "www.change.example.",
            Type = DnsResourceRecordTypeEnum.A,
            TimeToLiveSeconds = 300,
            DataJson = "{\"Address\":\"192.0.2.1\"}",
        };

    /// <summary>
    ///     Adding a zone and updating a record in one save notifies both types once, and the rows
    ///     are already readable from another scope when the callback runs.
    /// </summary>
    [Fact]
    public async Task SaveChanges_AddingAZoneAndUpdatingARecord_NotifiesBothTypesOnceAfterTheCommit()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        ZoneEntity existingZone = NewZone("existing.example.");
        RecordEntity record = NewRecord(existingZone.Id);
        await using (AsyncServiceScope seed = database.Services.CreateAsyncScope())
        {
            await seed.ServiceProvider.GetRequiredService<IZoneRepository>().AddAsync(existingZone);
            await seed.ServiceProvider.GetRequiredService<IRecordRepository>().AddAsync(record);
            await seed.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        IEntityChangeNotifier notifier =
            database.Services.GetRequiredService<IEntityChangeNotifier>();
        List<IReadOnlyCollection<Type>> calls = [];
        bool zoneVisibleInCallback = false;
        ZoneEntity newZone = NewZone("added.example.");
        using IDisposable subscription = notifier.Subscribe(
            [],
            changed =>
            {
                calls.Add(changed);
                using IServiceScope check = database.Services.CreateScope();
                zoneVisibleInCallback =
                    check
                        .ServiceProvider.GetRequiredService<IZoneRepository>()
                        .FindByNameAsync(newZone.Name)
                        .GetAwaiter()
                        .GetResult() != null;
            }
        );

        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
        IRecordRepository records = scope.ServiceProvider.GetRequiredService<IRecordRepository>();
        RecordEntity tracked = await records.GetByIdAsync(record.Id);
        tracked.TimeToLiveSeconds = 60;
        records.Update(tracked);
        await scope.ServiceProvider.GetRequiredService<IZoneRepository>().AddAsync(newZone);
        Assert.Empty(calls);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

        IReadOnlyCollection<Type> only = Assert.Single(calls);
        Assert.Equal(2, only.Count);
        Assert.Contains(typeof(ZoneEntity), only);
        Assert.Contains(typeof(RecordEntity), only);
        Assert.True(zoneVisibleInCallback);
    }

    /// <summary>
    ///     A save the database rejects (a duplicate primary key) notifies nothing.
    /// </summary>
    [Fact]
    public async Task SaveChanges_ThatFails_NotifiesNothing()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        ZoneEntity zone = NewZone("failing.example.");
        await using (AsyncServiceScope seed = database.Services.CreateAsyncScope())
        {
            await seed.ServiceProvider.GetRequiredService<IZoneRepository>().AddAsync(zone);
            await seed.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        int calls = 0;
        using IDisposable subscription = database
            .Services.GetRequiredService<IEntityChangeNotifier>()
            .Subscribe([], _ => calls++);

        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
        ZoneEntity duplicate = NewZone("other.example.");
        duplicate.Id = zone.Id;
        await scope.ServiceProvider.GetRequiredService<IZoneRepository>().AddAsync(duplicate);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync()
        );
        Assert.Equal(0, calls);
    }

    /// <summary>
    ///     A subscriber for other entity types is not called.
    /// </summary>
    [Fact]
    public async Task SaveChanges_ToAnotherType_DoesNotCallASubscriberForOtherTypes()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        int recordCalls = 0;
        int zoneCalls = 0;
        IEntityChangeNotifier notifier =
            database.Services.GetRequiredService<IEntityChangeNotifier>();
        using IDisposable records = notifier.Subscribe([typeof(RecordEntity)], _ => recordCalls++);
        using IDisposable zones = notifier.Subscribe([typeof(ZoneEntity)], _ => zoneCalls++);

        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<IZoneRepository>()
            .AddAsync(NewZone("types.example."));
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

        Assert.Equal(0, recordCalls);
        Assert.Equal(1, zoneCalls);
    }

    /// <summary>
    ///     A subscriber that throws does not stop the others and does not fail the save.
    /// </summary>
    [Fact]
    public async Task SaveChanges_WithAThrowingSubscriber_StillCallsTheOthersAndSucceeds()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        int calls = 0;
        IEntityChangeNotifier notifier =
            database.Services.GetRequiredService<IEntityChangeNotifier>();
        using IDisposable throwing = notifier.Subscribe(
            [],
            _ => throw new InvalidOperationException("subscriber failure")
        );
        using IDisposable counting = notifier.Subscribe([], _ => calls++);

        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
        ZoneEntity zone = NewZone("throwing.example.");
        await scope.ServiceProvider.GetRequiredService<IZoneRepository>().AddAsync(zone);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

        Assert.Equal(1, calls);
        await using AsyncServiceScope check = database.Services.CreateAsyncScope();
        Assert.NotNull(
            await check
                .ServiceProvider.GetRequiredService<IZoneRepository>()
                .FindByNameAsync(zone.Name)
        );
    }

    /// <summary>
    ///     A disposed subscription is no longer called.
    /// </summary>
    [Fact]
    public async Task Subscribe_WhenDisposed_StopsCallingTheSubscriber()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        int calls = 0;
        IDisposable subscription = database
            .Services.GetRequiredService<IEntityChangeNotifier>()
            .Subscribe([], _ => calls++);
        subscription.Dispose();

        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<IZoneRepository>()
            .AddAsync(NewZone("disposed.example."));
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

        Assert.Equal(0, calls);
    }

    /// <summary>
    ///     A filter-list refresh reports the entry type, and on the relational engines (where the
    ///     replace runs at once, inside the caller's transaction) only after the commit; a rolled
    ///     back refresh reports nothing.
    /// </summary>
    [Fact]
    public async Task ReplaceEntriesAsync_ReportsTheEntryTypeAfterTheCommitAndNothingOnRollback()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        DnsFilterListEntity list = new()
        {
            Id = Guid.NewGuid(),
            Name = "notified",
            Kind = DnsFilterListKindEnum.Domain,
            Action = DnsFilterListActionEnum.Block,
            Source = DnsFilterListSourceEnum.Subscribed,
            SubscriptionUrl = "https://lists.example/list.txt",
        };
        await using (AsyncServiceScope seed = database.Services.CreateAsyncScope())
        {
            await seed
                .ServiceProvider.GetRequiredService<IDnsFilterListRepository>()
                .AddAsync(list);
            await seed.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        List<IReadOnlyCollection<Type>> calls = [];
        using IDisposable subscription = database
            .Services.GetRequiredService<IEntityChangeNotifier>()
            .Subscribe([typeof(DnsFilterListEntryEntity)], calls.Add);
        List<DnsFilterListReplacementEntry> replacement = [new("a.example", false)];

        // Rolled back: nothing is reported.
        await using (AsyncServiceScope rolledBack = database.Services.CreateAsyncScope())
        {
            IUnitOfWorkTransactionFactory factory =
                rolledBack.ServiceProvider.GetService<IUnitOfWorkTransactionFactory>();

            if (factory is not null)
            {
                await using IUnitOfWorkTransaction transaction = await factory.BeginAsync();
                await rolledBack
                    .ServiceProvider.GetRequiredService<IDnsFilterListEntryRepository>()
                    .ReplaceEntriesAsync(list.Id, replacement);
                await rolledBack
                    .ServiceProvider.GetRequiredService<IUnitOfWork>()
                    .SaveChangesAsync();
            }
        }

        Assert.Empty(calls);

        await using AsyncServiceScope committed = database.Services.CreateAsyncScope();
        IUnitOfWorkTransactionFactory commitFactory =
            committed.ServiceProvider.GetService<IUnitOfWorkTransactionFactory>();
        IUnitOfWorkTransaction commitTransaction = commitFactory is null
            ? null
            : await commitFactory.BeginAsync();
        await committed
            .ServiceProvider.GetRequiredService<IDnsFilterListEntryRepository>()
            .ReplaceEntriesAsync(list.Id, replacement);

        if (commitTransaction is not null)
        {
            Assert.Empty(calls);
            await committed.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
            Assert.Empty(calls);
            await commitTransaction.CommitAsync();
            await commitTransaction.DisposeAsync();
        }
        else
        {
            await committed.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        IReadOnlyCollection<Type> only = Assert.Single(calls);
        Assert.Equal(typeof(DnsFilterListEntryEntity), Assert.Single(only));
    }
}

/// <summary>This class runs <see cref="EntityChangeNotificationTestsBase" /> on SQLite (no Docker).</summary>
[Collection(SqliteCollection.Name)]
public sealed class SqliteEntityChangeNotificationTests(SqliteEngineFixture fixture)
    : EntityChangeNotificationTestsBase(fixture);

/// <summary>This class runs <see cref="EntityChangeNotificationTestsBase" /> on PostgreSQL (Docker).</summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class PostgreSqlEntityChangeNotificationTests(PostgreSqlEngineFixture fixture)
    : EntityChangeNotificationTestsBase(fixture);

/// <summary>This class runs <see cref="EntityChangeNotificationTestsBase" /> on MySQL (Docker).</summary>
[Collection(MySqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MySqlEntityChangeNotificationTests(MySqlEngineFixture fixture)
    : EntityChangeNotificationTestsBase(fixture);

/// <summary>This class runs <see cref="EntityChangeNotificationTestsBase" /> on SQL Server (Docker).</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class SqlServerEntityChangeNotificationTests(SqlServerEngineFixture fixture)
    : EntityChangeNotificationTestsBase(fixture);

/// <summary>This class runs <see cref="EntityChangeNotificationTestsBase" /> on a MongoDB replica set (Docker).</summary>
[Collection(MongoDbCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoDbEntityChangeNotificationTests(MongoDbEngineFixture fixture)
    : EntityChangeNotificationTestsBase(fixture);

/// <summary>This class runs <see cref="EntityChangeNotificationTestsBase" /> on a standalone MongoDB (Docker).</summary>
[Collection(MongoDbStandaloneCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoDbStandaloneEntityChangeNotificationTests(
    MongoDbStandaloneEngineFixture fixture
) : EntityChangeNotificationTestsBase(fixture);
