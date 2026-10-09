using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.Integration.Tests.Fixtures;

// Define our namespace.
namespace Ninlil.Dns.Data.Integration.Tests.Repository;

/// <summary>
///     This class holds the bulk-insert test that runs against every engine: one subclass per
///     engine supplies the fixture. <c>AddRangeAsync</c> of 1,000 query-log entries followed by
///     one save stores all 1,000. The entity-specific part is the number of writes the save
///     reports: Entity Framework Core reports one per row, MongoDB stages one <c>InsertMany</c>
///     for the whole range, so it reports one.
/// </summary>
public abstract class AddRangeTestsBase(EngineFixtureBase fixture, int expectedSaveCount)
{
    private const int Count = 1000;

    /// <summary>
    ///     Adding a range stores every entity after a single save.
    /// </summary>
    [Fact]
    public async Task AddRangeAsync_OfAThousandEntries_StoresAllOfThem()
    {
        await using EngineDatabase database = await fixture.CreateDatabaseAsync();
        DateTimeOffset baseTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        List<DnsQueryLogEntryEntity> entries =
        [
            .. Enumerable
                .Range(0, Count)
                .Select(index => new DnsQueryLogEntryEntity
                {
                    Id = Guid.CreateVersion7(baseTime.AddSeconds(index)),
                    CreatedAtUtc = baseTime.AddSeconds(index),
                    OccurredAtUtc = baseTime.AddSeconds(index),
                    ClientAddress = "203.0.113.10",
                    Protocol = DnsTransportProtocolEnum.Udp,
                    QueriedName = $"host{index}.example",
                    QueriedType = DnsResourceRecordTypeEnum.A,
                    ResponseCode = DnsResponseCodeEnum.NoError,
                    AnswerCount = 1,
                }),
        ];

        int saved;
        await using (AsyncServiceScope scope = database.Services.CreateAsyncScope())
        {
            await scope
                .ServiceProvider.GetRequiredService<IDnsQueryLogRepository>()
                .AddRangeAsync(entries);
            saved = await scope
                .ServiceProvider.GetRequiredService<IUnitOfWork>()
                .SaveChangesAsync();
        }

        await using AsyncServiceScope check = database.Services.CreateAsyncScope();
        IDnsQueryLogRepository repository =
            check.ServiceProvider.GetRequiredService<IDnsQueryLogRepository>();
        Assert.Equal(Count, await repository.CountAsync(new DnsQueryLogSearchCriteria()));
        Assert.Equal(expectedSaveCount, saved);
    }
}

/// <summary>This class runs <see cref="AddRangeTestsBase" /> on SQLite (no Docker).</summary>
[Collection(SqliteCollection.Name)]
public sealed class SqliteAddRangeTests(SqliteEngineFixture fixture)
    : AddRangeTestsBase(fixture, 1000);

/// <summary>This class runs <see cref="AddRangeTestsBase" /> on PostgreSQL (Docker).</summary>
[Collection(PostgreSqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class PostgreSqlAddRangeTests(PostgreSqlEngineFixture fixture)
    : AddRangeTestsBase(fixture, 1000);

/// <summary>This class runs <see cref="AddRangeTestsBase" /> on MySQL (Docker).</summary>
[Collection(MySqlCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MySqlAddRangeTests(MySqlEngineFixture fixture)
    : AddRangeTestsBase(fixture, 1000);

/// <summary>This class runs <see cref="AddRangeTestsBase" /> on SQL Server (Docker).</summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class SqlServerAddRangeTests(SqlServerEngineFixture fixture)
    : AddRangeTestsBase(fixture, 1000);

/// <summary>
///     This class runs <see cref="AddRangeTestsBase" /> on a MongoDB replica set (Docker); the
///     whole range is one staged operation.
/// </summary>
[Collection(MongoDbCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoDbAddRangeTests(MongoDbEngineFixture fixture)
    : AddRangeTestsBase(fixture, 1);

/// <summary>
///     This class runs <see cref="AddRangeTestsBase" /> on a standalone MongoDB (Docker).
/// </summary>
[Collection(MongoDbStandaloneCollection.Name)]
[Trait("Category", "RequiresDocker")]
public sealed class MongoDbStandaloneAddRangeTests(MongoDbStandaloneEngineFixture fixture)
    : AddRangeTestsBase(fixture, 1);
