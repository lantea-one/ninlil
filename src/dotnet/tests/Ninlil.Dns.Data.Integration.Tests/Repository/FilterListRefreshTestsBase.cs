using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Repository;

/// <summary>
///     This class holds the tests of a filter-list refresh as
///     <c>DnsFilterListExternalRefreshService</c> performs it on the relational engines:
///     <see cref="IDnsFilterListEntryRepository.ReplaceEntriesAsync" /> inside a unit-of-work
///     transaction, followed by one save. One subclass per relational engine supplies the fixture
///     and a fresh database. MongoDB has no transaction factory and is not covered here.
/// </summary>
public abstract class FilterListRefreshTestsBase(EngineFixtureBase fixture)
{
    private const int OldCount = 1000;
    private const int NewCount = 2000;

    /// <summary>
    ///     Seeds a list with <see cref="OldCount" /> entries and returns its identifier, plus a
    ///     second list whose entries a refresh must not touch.
    /// </summary>
    private static async Task<(Guid ListId, Guid OtherListId)> SeedAsync(EngineDatabase database)
    {
        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
        IDnsFilterListRepository lists =
            scope.ServiceProvider.GetRequiredService<IDnsFilterListRepository>();
        IDnsFilterListEntryRepository entries =
            scope.ServiceProvider.GetRequiredService<IDnsFilterListEntryRepository>();

        DnsFilterListEntity list = NewList("refreshed");
        DnsFilterListEntity other = NewList("untouched");
        await lists.AddAsync(list);
        await lists.AddAsync(other);
        await entries.AddRangeAsync([
            .. Enumerable.Range(0, OldCount).Select(index => NewEntry(list.Id, $"old{index}")),
            .. Enumerable.Range(0, 10).Select(index => NewEntry(other.Id, $"other{index}")),
        ]);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        return (list.Id, other.Id);
    }

    private static DnsFilterListEntity NewList(string name) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Kind = DnsFilterListKindEnum.Domain,
            Action = DnsFilterListActionEnum.Block,
            Source = DnsFilterListSourceEnum.Subscribed,
            SubscriptionUrl = "https://lists.example/list.txt",
        };

    private static DnsFilterListEntryEntity NewEntry(Guid listId, string value) =>
        new()
        {
            Id = Guid.NewGuid(),
            ListId = listId,
            Value = value,
        };

    /// <summary>
    ///     A committed refresh leaves exactly the new set, and no other list changes.
    /// </summary>
    [Fact]
    public async Task ReplaceEntriesAsync_InATransaction_LeavesExactlyTheNewSet()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        (Guid listId, Guid otherId) = await SeedAsync(database);
        List<DnsFilterListReplacementEntry> replacement =
        [
            .. Enumerable
                .Range(0, NewCount)
                .Select(index => new DnsFilterListReplacementEntry($"new{index}", index == 0)),
        ];

        await using (AsyncServiceScope scope = database.Services.CreateAsyncScope())
        {
            IServiceProvider services = scope.ServiceProvider;
            await using IUnitOfWorkTransaction transaction = await services
                .GetRequiredService<IUnitOfWorkTransactionFactory>()
                .BeginAsync();
            await services
                .GetRequiredService<IDnsFilterListEntryRepository>()
                .ReplaceEntriesAsync(listId, replacement);
            await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await using AsyncServiceScope check = database.Services.CreateAsyncScope();
        IDnsFilterListEntryRepository entries =
            check.ServiceProvider.GetRequiredService<IDnsFilterListEntryRepository>();
        IReadOnlyList<DnsFilterListEntryEntity> after = await entries.ListByListIdAsync(listId);
        Assert.Equal(NewCount, after.Count);
        Assert.All(after, entry => Assert.StartsWith("new", entry.Value));
        Assert.Equal(NewCount, after.Select(entry => entry.Value).Distinct().Count());
        // The converted columns round-trip: the exception flag, the entry kind and the instant.
        DnsFilterListEntryEntity exception = Assert.Single(after, entry => entry.IsException);
        Assert.Equal("new0", exception.Value);
        Assert.All(after, entry => Assert.Equal(DnsFilterEntryKindEnum.Domain, entry.Kind));
        Assert.All(
            after,
            entry =>
                Assert.InRange(
                    entry.CreatedAtUtc,
                    DateTimeOffset.UtcNow.AddMinutes(-5),
                    DateTimeOffset.UtcNow.AddMinutes(1)
                )
        );
        Assert.Equal(10, await entries.CountByListIdAsync(otherId));
    }

    /// <summary>
    ///     A refresh whose replace fails part way (a null value breaks the NOT NULL column in the
    ///     last insert statement, after earlier statements have run) rolls back, so the old
    ///     entries remain.
    /// </summary>
    [Fact]
    public async Task ReplaceEntriesAsync_WhenTheInsertFailsPartWay_LeavesTheOldEntries()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        (Guid listId, _) = await SeedAsync(database);
        List<DnsFilterListReplacementEntry> replacement =
        [
            .. Enumerable
                .Range(0, NewCount)
                .Select(index => new DnsFilterListReplacementEntry($"new{index}")),
            new DnsFilterListReplacementEntry(null),
        ];

        await using (AsyncServiceScope scope = database.Services.CreateAsyncScope())
        {
            IServiceProvider services = scope.ServiceProvider;
            await using IUnitOfWorkTransaction transaction = await services
                .GetRequiredService<IUnitOfWorkTransactionFactory>()
                .BeginAsync();
            await Assert.ThrowsAnyAsync<DbException>(() =>
                services
                    .GetRequiredService<IDnsFilterListEntryRepository>()
                    .ReplaceEntriesAsync(listId, replacement)
            );
        }

        await AssertOldEntriesRemainAsync(database, listId);
    }

    /// <summary>
    ///     A refresh whose replace succeeds but whose save fails (the list update breaks its
    ///     NOT NULL name) rolls back, so the old entries remain.
    /// </summary>
    [Fact]
    public async Task ReplaceEntriesAsync_WhenTheSaveFails_LeavesTheOldEntries()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        (Guid listId, _) = await SeedAsync(database);
        List<DnsFilterListReplacementEntry> replacement =
        [
            .. Enumerable
                .Range(0, NewCount)
                .Select(index => new DnsFilterListReplacementEntry($"new{index}")),
        ];

        await using (AsyncServiceScope scope = database.Services.CreateAsyncScope())
        {
            IServiceProvider services = scope.ServiceProvider;
            await using IUnitOfWorkTransaction transaction = await services
                .GetRequiredService<IUnitOfWorkTransactionFactory>()
                .BeginAsync();
            await services
                .GetRequiredService<IDnsFilterListEntryRepository>()
                .ReplaceEntriesAsync(listId, replacement);

            IDnsFilterListRepository lists =
                services.GetRequiredService<IDnsFilterListRepository>();
            DnsFilterListEntity tracked = await lists.GetByIdAsync(listId);
            tracked.Name = null;
            lists.Update(tracked);
            await Assert.ThrowsAnyAsync<DbUpdateException>(() =>
                services.GetRequiredService<IUnitOfWork>().SaveChangesAsync()
            );
        }

        await AssertOldEntriesRemainAsync(database, listId);
    }

    private static async Task AssertOldEntriesRemainAsync(EngineDatabase database, Guid listId)
    {
        await using AsyncServiceScope check = database.Services.CreateAsyncScope();
        IReadOnlyList<DnsFilterListEntryEntity> after = await check
            .ServiceProvider.GetRequiredService<IDnsFilterListEntryRepository>()
            .ListByListIdAsync(listId);
        Assert.Equal(OldCount, after.Count);
        Assert.All(after, entry => Assert.StartsWith("old", entry.Value));
    }
}

/// <summary>This class runs <see cref="FilterListRefreshTestsBase" /> on SQLite (no Docker).</summary>
[Collection(SqliteCollection.Name)]
public sealed class SqliteFilterListRefreshTests(SqliteEngineFixture fixture)
    : FilterListRefreshTestsBase(fixture);

/// <summary>This class runs <see cref="FilterListRefreshTestsBase" /> on PostgreSQL (Docker).</summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class PostgreSqlFilterListRefreshTests(PostgreSqlEngineFixture fixture)
    : FilterListRefreshTestsBase(fixture);

/// <summary>This class runs <see cref="FilterListRefreshTestsBase" /> on MySQL (Docker).</summary>
[Collection(MySqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MySqlFilterListRefreshTests(MySqlEngineFixture fixture)
    : FilterListRefreshTestsBase(fixture);

/// <summary>This class runs <see cref="FilterListRefreshTestsBase" /> on SQL Server (Docker).</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class SqlServerFilterListRefreshTests(SqlServerEngineFixture fixture)
    : FilterListRefreshTestsBase(fixture);
