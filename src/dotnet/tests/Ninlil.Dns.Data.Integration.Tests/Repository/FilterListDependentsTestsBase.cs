using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Repository;

/// <summary>
///     This class holds the tests of the rule that deleting a filter list removes its entries
///     and every assignment (<see cref="DnsFilterListDependents" />), run against every engine:
///     one subclass per engine setup supplies the fixture. The relational engines wrap the
///     removal in a unit-of-work transaction; MongoDB registers no factory and runs the staged
///     entry delete at save time.
/// </summary>
public abstract class FilterListDependentsTestsBase(EngineFixtureBase fixture)
{
    private const int EntryCount = 1000;

    /// <summary>
    ///     Seeds two lists, each with <see cref="EntryCount" /> entries, a client-group
    ///     assignment and a client assignment, and returns the two identifiers.
    /// </summary>
    private static async Task<(Guid ListId, Guid OtherListId)> SeedAsync(EngineDatabase database)
    {
        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
        IServiceProvider services = scope.ServiceProvider;
        IDnsFilterListRepository lists = services.GetRequiredService<IDnsFilterListRepository>();
        IDnsFilterListEntryRepository entries =
            services.GetRequiredService<IDnsFilterListEntryRepository>();

        Guid[] ids = [Guid.NewGuid(), Guid.NewGuid()];
        foreach (Guid id in ids)
        {
            await lists.AddAsync(
                new DnsFilterListEntity
                {
                    Id = id,
                    Name = $"list-{id:N}",
                    Kind = DnsFilterListKindEnum.Domain,
                    Action = DnsFilterListActionEnum.Block,
                    Source = DnsFilterListSourceEnum.Local,
                }
            );
            await entries.AddRangeAsync([
                .. Enumerable
                    .Range(0, EntryCount)
                    .Select(index => new DnsFilterListEntryEntity
                    {
                        Id = Guid.NewGuid(),
                        ListId = id,
                        Value = $"e{index}.example",
                    }),
            ]);
            await services
                .GetRequiredService<IDnsClientGroupListAssignmentRepository>()
                .AddAsync(
                    new DnsClientGroupListAssignmentEntity
                    {
                        Id = Guid.NewGuid(),
                        ClientGroupId = Guid.NewGuid(),
                        ListId = id,
                    }
                );
            await services
                .GetRequiredService<IDnsClientListAssignmentRepository>()
                .AddAsync(
                    new DnsClientListAssignmentEntity
                    {
                        Id = Guid.NewGuid(),
                        ClientId = Guid.NewGuid(),
                        ListId = id,
                    }
                );
        }

        await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        return (ids[0], ids[1]);
    }

    /// <summary>
    ///     Counts the rows that refer to a list: entries, group assignments, client assignments.
    /// </summary>
    private static async Task<(int Entries, int Groups, int Clients)> CountAsync(
        EngineDatabase database,
        Guid listId
    )
    {
        await using AsyncServiceScope scope = database.Services.CreateAsyncScope();
        IServiceProvider services = scope.ServiceProvider;
        return (
            await services
                .GetRequiredService<IDnsFilterListEntryRepository>()
                .CountByListIdAsync(listId),
            (
                await services
                    .GetRequiredService<IDnsClientGroupListAssignmentRepository>()
                    .ListAsync(assignment => assignment.ListId == listId)
            ).Count,
            (
                await services
                    .GetRequiredService<IDnsClientListAssignmentRepository>()
                    .ListAsync(assignment => assignment.ListId == listId)
            ).Count
        );
    }

    /// <summary>
    ///     Removing a list through the helper leaves it with no entries or assignments, and the
    ///     other list is untouched.
    /// </summary>
    [Fact]
    public async Task StageRemovalAsync_ThenRemovingTheList_LeavesNothingOfItAndTouchesNoOtherList()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        (Guid listId, Guid otherId) = await SeedAsync(database);

        await using (AsyncServiceScope scope = database.Services.CreateAsyncScope())
        {
            IServiceProvider services = scope.ServiceProvider;
            IUnitOfWorkTransactionFactory factory =
                services.GetService<IUnitOfWorkTransactionFactory>();
            await using IUnitOfWorkTransaction transaction = factory is null
                ? null
                : await factory.BeginAsync();

            IDnsFilterListRepository lists =
                services.GetRequiredService<IDnsFilterListRepository>();
            await DnsFilterListDependents.StageRemovalAsync(
                listId,
                services.GetRequiredService<IDnsClientGroupListAssignmentRepository>(),
                services.GetRequiredService<IDnsClientListAssignmentRepository>(),
                services.GetRequiredService<IDnsFilterListEntryRepository>()
            );
            lists.Remove(await lists.GetByIdAsync(listId));
            await services.GetRequiredService<IUnitOfWork>().SaveChangesAsync();

            if (transaction is not null)
                await transaction.CommitAsync();
        }

        Assert.Equal((0, 0, 0), await CountAsync(database, listId));
        Assert.Equal((EntryCount, 1, 1), await CountAsync(database, otherId));
        await using AsyncServiceScope check = database.Services.CreateAsyncScope();
        IDnsFilterListRepository checkLists =
            check.ServiceProvider.GetRequiredService<IDnsFilterListRepository>();
        Assert.Null(await checkLists.GetByIdAsync(listId));
        Assert.NotNull(await checkLists.GetByIdAsync(otherId));
    }

    /// <summary>
    ///     A save that fails after the helper ran (a tracked list gets a null name) rolls the
    ///     whole removal back on a relational engine, so the entries and assignments remain.
    ///     MongoDB has no transaction factory and is not covered here.
    /// </summary>
    [Fact]
    public async Task StageRemovalAsync_WhenTheSaveFails_RollsTheRemovalBackOnARelationalEngine()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        (Guid listId, Guid otherId) = await SeedAsync(database);

        await using (AsyncServiceScope scope = database.Services.CreateAsyncScope())
        {
            IServiceProvider services = scope.ServiceProvider;
            IUnitOfWorkTransactionFactory factory =
                services.GetService<IUnitOfWorkTransactionFactory>();
            if (factory is null)
                return;

            await using IUnitOfWorkTransaction transaction = await factory.BeginAsync();
            IDnsFilterListRepository lists =
                services.GetRequiredService<IDnsFilterListRepository>();
            await DnsFilterListDependents.StageRemovalAsync(
                listId,
                services.GetRequiredService<IDnsClientGroupListAssignmentRepository>(),
                services.GetRequiredService<IDnsClientListAssignmentRepository>(),
                services.GetRequiredService<IDnsFilterListEntryRepository>()
            );
            DnsFilterListEntity other = await lists.GetByIdAsync(otherId);
            other.Name = null;
            lists.Update(other);
            lists.Remove(await lists.GetByIdAsync(listId));
            await Assert.ThrowsAnyAsync<Exception>(() =>
                services.GetRequiredService<IUnitOfWork>().SaveChangesAsync()
            );
        }

        Assert.Equal((EntryCount, 1, 1), await CountAsync(database, listId));
    }
}

/// <summary>This class runs <see cref="FilterListDependentsTestsBase" /> on SQLite (no Docker).</summary>
[Collection(SqliteCollection.Name)]
public sealed class SqliteFilterListDependentsTests(SqliteEngineFixture fixture)
    : FilterListDependentsTestsBase(fixture);

/// <summary>This class runs <see cref="FilterListDependentsTestsBase" /> on PostgreSQL (Docker).</summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class PostgreSqlFilterListDependentsTests(PostgreSqlEngineFixture fixture)
    : FilterListDependentsTestsBase(fixture);

/// <summary>This class runs <see cref="FilterListDependentsTestsBase" /> on MySQL (Docker).</summary>
[Collection(MySqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MySqlFilterListDependentsTests(MySqlEngineFixture fixture)
    : FilterListDependentsTestsBase(fixture);

/// <summary>This class runs <see cref="FilterListDependentsTestsBase" /> on SQL Server (Docker).</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class SqlServerFilterListDependentsTests(SqlServerEngineFixture fixture)
    : FilterListDependentsTestsBase(fixture);

/// <summary>This class runs <see cref="FilterListDependentsTestsBase" /> on a MongoDB replica set (Docker).</summary>
[Collection(MongoDbCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoDbFilterListDependentsTests(MongoDbEngineFixture fixture)
    : FilterListDependentsTestsBase(fixture);

/// <summary>This class runs <see cref="FilterListDependentsTestsBase" /> on a standalone MongoDB (Docker).</summary>
[Collection(MongoDbStandaloneCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoDbStandaloneFilterListDependentsTests(
    MongoDbStandaloneEngineFixture fixture
) : FilterListDependentsTestsBase(fixture);
